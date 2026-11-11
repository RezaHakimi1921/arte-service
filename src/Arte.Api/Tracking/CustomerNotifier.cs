using System.Security.Cryptography;
using System.Text.Json;
using Arte.Api.Sms;
using Arte.Api.Sms.SmartX;
using Arte.Core.Cases;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Tracking;

public static class TrackingCodes
{
    // No look-alike characters (0/o, 1/l/i): customers may read it aloud.
    private const string Alphabet = "abcdefghjkmnpqrstuvwxyz23456789";

    /// <summary>10 characters ≈ 49 bits: unguessable at the tracking endpoint's rate limit.</summary>
    public static string New() => RandomNumberGenerator.GetString(Alphabet, 10);
}

/// <summary>
/// SMS to the customer at the moments the business chose (case opened, ready, delivered), each carrying the
/// tracking link. Off unless the business switched customer SMS on; never for sample cases; a failed or
/// unconfigured SMS never breaks the action that triggered it. Each send, including one that could not go out (with the reason), is written to the case timeline.
/// </summary>
public sealed class CustomerNotifier(ArteDbContext db, ISmsProvider sms, IClock clock, ILogger<CustomerNotifier> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static readonly IReadOnlyDictionary<string, string> KindWords = new Dictionary<string, string>
    {
        ["motorcycle"] = "موتور", ["car"] = "خودرو", ["suv"] = "خودرو", ["van"] = "خودرو", ["pickup"] = "خودرو",
    };

    /// <summary>A pattern variable is short; cut long names and titles rather than have the provider refuse.</summary>
    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd();

    /// <summary>
    /// The link SMS sent by hand from the case page. Returns null when sent, otherwise why not (Persian, for the user).
    /// </summary>
    /// <summary>Minimum time between two link SMS for the same case.</summary>
    public static readonly TimeSpan LinkCooldown = TimeSpan.FromMinutes(20);

    public async Task<string?> SendLinkAsync(Case c, CancellationToken ct)
    {
        if (c.IsSample || c.TrackingCode is null) return "برای پرونده‌ی نمونه پیامک فرستاده نمی‌شود.";
        var t = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == c.TenantId, ct);
        if (!t.CustomerSmsEnabled) return "پیامک به مشتری در تنظیمات ← پیامک و پیگیری مشتری خاموش است.";

        // The link may be sent again only after a cool-down (a customer hearing from the shop 4 times is a bad sign
        // for the shop). A «case opened» SMS carries the same link, so it counts as well; failed sends do not.
        var since = clock.UtcNow - LinkCooldown;
        var recent = await db.CaseEvents.AsNoTracking()
            .Where(e => e.CaseId == c.Id && e.Type == "customer.sms" && e.OccurredAt > since)
            .OrderByDescending(e => e.OccurredAt).Take(10).ToListAsync(ct);
        var last = recent.FirstOrDefault(e => e.Data is not null
            && e.Data.RootElement.TryGetProperty("sent", out var ok) && ok.ValueKind == JsonValueKind.True
            && e.Data.RootElement.TryGetProperty("kind", out var kind) && kind.GetString() is MessageKeys.CaseLink or MessageKeys.CaseOpened);
        if (last is not null)
        {
            var wait = (int)Math.Ceiling((last.OccurredAt + LinkCooldown - clock.UtcNow).TotalMinutes);
            return $"لینک همین چند دقیقه پیش برای مشتری پیامک شد. {Math.Max(wait, 1)} دقیقه‌ی دیگر می‌توانید دوباره بفرستید.";
        }

        var result = await SendAsync(c, t, MessageKeys.CaseLink, ct);
        if (SoftFailNotice(result) is { } soft) return soft;
        return result switch
        {
            { Accepted: true } => null,
            { Error: SmartXSmsProvider.OutsideHours } => "پیامک فقط از ساعت ۸ تا ۲۳ فرستاده می‌شود؛ صبح دوباره امتحان کنید.",
            { Error: SmartXSmsProvider.NotApproved } => "متن این پیامک هنوز توسط مدیریت آرته تأیید نشده است.",
            { Error: "template_not_configured" } => "پیامک لینک هنوز فعال نشده است؛ فعلاً لینک را با «اشتراک‌گذاری» بفرستید.",
            _ => "ارسال پیامک انجام نشد. کمی بعد دوباره تلاش کنید.",
        };
    }

    /// <summary>Returns a short Persian notice when the action succeeded but SMS was skipped (credit / licence).</summary>
    public async Task<string?> NotifyAsync(Case c, string messageKey, CancellationToken ct)
    {
        try
        {
            if (c.IsSample || c.TrackingCode is null) return null;
            var t = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == c.TenantId, ct);
            var wanted = messageKey switch
            {
                MessageKeys.CaseOpened => t.SmsOnOpened,
                MessageKeys.CaseReady => t.SmsOnReady,
                MessageKeys.CaseDelivered => t.SmsOnDelivered,
                MessageKeys.CaseWaitingPart => t.SmsOnWaitingPart,
                _ => false,
            };
            if (!t.CustomerSmsEnabled || !wanted) return null;
            // Where the survey add-on sends its SMS after delivery, that one replaces the «delivered» SMS.
            if (messageKey == MessageKeys.CaseDelivered && t.SurveyEnabled && t.SurveySendOn) return null;
            return SoftFailNotice(await SendAsync(c, t, messageKey, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Customer SMS {MessageKey} for case {CaseId} failed", messageKey, c.Id);
            return null;
        }
    }

    public static string? SoftFailNotice(SmsSendResult result) => result switch
    {
        { Accepted: true } => null,
        { Error: SmartXSmsProvider.SkippedNoCredit or SmsWalletService.InsufficientCredit } =>
            "این عملیات بدون پیامک انجام شد. لطفاً پنل پیامک خود را شارژ کنید.",
        { Error: SmartXSmsProvider.SkippedLicense } =>
            "به‌خاطر اتمام پرونده‌های رایگان، پیامک ارسال نشد. برای فعال‌سازی اشتراک با پشتیبانی آرته تماس بگیرید.",
        _ => null,
    };

    /// <summary>SMS to the master(s) that a finished job waits for review. Who: members who can approve work (cases.create), except the one who finished it.</summary>
    public async Task NotifyMastersAsync(Case c, Guid actorUserId, CancellationToken ct)
    {
        try
        {
            if (c.IsSample) return;
            var t = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == c.TenantId, ct);
            if (!t.SmsToMasterOnFinished) return;
            var members = await db.Memberships.AsNoTracking().Include(m => m.User).Where(m => m.IsActive && m.UserId != actorUserId).ToListAsync(ct);
            var mobiles = members.Where(m => m.Has(Arte.Core.Identity.Permissions.CasesCreate) && m.User is not null).Select(m => m.User!.Mobile).Distinct().Take(3).ToList();
            if (mobiles.Count == 0) return;
            var asset = c.AssetId is null ? null : await db.Assets.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking().SingleOrDefaultAsync(x => x.Id == c.AssetId, ct);
            var tokens = new Dictionary<string, string>
            {
                ["shop"] = Cut(t.Name, SmsLimits.Shop),
                ["name"] = "همکار",
                ["vehicle"] = Cut(asset is null ? "وسیله‌ی" : $"{KindWords.GetValueOrDefault(asset.Kind, "وسیله‌ی")} {asset.Title}", SmsLimits.Vehicle),
                ["code"] = $"CASE-{c.Number}",
            };
            foreach (var mobile in mobiles)
            {
                var result = await sms.SendAsync(new SmsRequest(mobile, MessageKeys.StaffWorkFinished, tokens, c.TenantId), ct);
                db.CaseEvents.Add(new CaseEvent
                {
                    TenantId = c.TenantId, CaseId = c.Id, Type = "staff.sms", OccurredAt = clock.UtcNow,
                    Data = JsonSerializer.SerializeToDocument(new { Kind = MessageKeys.StaffWorkFinished, Sent = result.Accepted, Error = result.Accepted ? null : result.Error }, Json),
                });
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Master SMS for case {CaseId} failed", c.Id);
        }
    }

    private async Task<SmsSendResult> SendAsync(Case c, Arte.Core.Tenancy.Tenant t, string messageKey, CancellationToken ct)
    {
        var customer = await db.Customers.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
            .SingleAsync(x => x.Id == c.CustomerId, ct);
        var asset = c.AssetId is null ? null : await db.Assets.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == c.AssetId, ct);
        var vehicle = asset is null ? "وسیله‌ی" : $"{KindWords.GetValueOrDefault(asset.Kind, "وسیله‌ی")} {asset.Title}";

        var tokens = new Dictionary<string, string>
        {
            ["shop"] = Cut(t.Name, SmsLimits.Shop),
            ["name"] = Cut(string.IsNullOrWhiteSpace(customer.FullName) ? "مشتری" : customer.FullName.Trim(), SmsLimits.Name),
            ["vehicle"] = Cut(vehicle, SmsLimits.Vehicle),
            ["code"] = c.TrackingCode!,
        };
        var result = await sms.SendAsync(new SmsRequest(customer.Mobile, messageKey, tokens, c.TenantId,
            new Dictionary<string, string> { ["CaseCode"] = $"CASE-{c.Number}" }), ct);
        db.CaseEvents.Add(new CaseEvent
        {
            TenantId = c.TenantId, CaseId = c.Id, Type = "customer.sms", OccurredAt = clock.UtcNow,
            Data = JsonSerializer.SerializeToDocument(new { Kind = messageKey, Sent = result.Accepted, Error = result.Accepted ? null : result.Error }, Json),
        });
        await db.SaveChangesAsync(ct);
        return result;
    }
}
