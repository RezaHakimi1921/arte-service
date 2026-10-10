using System.Security.Cryptography;
using System.Text.Json;
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
/// unconfigured SMS never breaks the action that triggered it. Each send is written to the case timeline.
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
    public async Task<string?> SendLinkAsync(Case c, CancellationToken ct)
    {
        if (c.IsSample || c.TrackingCode is null) return "برای پرونده‌ی نمونه پیامک فرستاده نمی‌شود.";
        var t = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == c.TenantId, ct);
        if (!t.CustomerSmsEnabled) return "پیامک به مشتری در تنظیمات ← پیامک و پیگیری مشتری خاموش است.";
        var result = await SendAsync(c, t, MessageKeys.CaseLink, ct);
        return result switch
        {
            { Accepted: true } => null,
            { Error: "template_not_configured" } => "پیامک لینک هنوز فعال نشده است؛ فعلاً لینک را با «اشتراک‌گذاری» بفرستید.",
            _ => "ارسال پیامک انجام نشد. کمی بعد دوباره تلاش کنید.",
        };
    }

    public async Task NotifyAsync(Case c, string messageKey, CancellationToken ct)
    {
        try
        {
            if (c.IsSample || c.TrackingCode is null) return;
            var t = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == c.TenantId, ct);
            var wanted = messageKey switch
            {
                MessageKeys.CaseOpened => t.SmsOnOpened,
                MessageKeys.CaseReady => t.SmsOnReady,
                MessageKeys.CaseDelivered => t.SmsOnDelivered,
                _ => false,
            };
            if (!t.CustomerSmsEnabled || !wanted) return;
            await SendAsync(c, t, messageKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Customer SMS {MessageKey} for case {CaseId} failed", messageKey, c.Id);
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
            ["shop"] = Cut(t.Name, 60),
            ["name"] = Cut(string.IsNullOrWhiteSpace(customer.FullName) ? "مشتری" : customer.FullName.Trim(), 60),
            ["vehicle"] = Cut(vehicle, 120),
            ["code"] = c.TrackingCode!,
        };
        var result = await sms.SendTemplateAsync(customer.Mobile, messageKey, tokens, ct);
        if (!result.Accepted && result.Error == "template_not_configured") return result;   // not set up yet: say nothing

        db.CaseEvents.Add(new CaseEvent
        {
            TenantId = c.TenantId, CaseId = c.Id, Type = "customer.sms", OccurredAt = clock.UtcNow,
            Data = JsonSerializer.SerializeToDocument(new { Kind = messageKey, Sent = result.Accepted }, Json),
        });
        await db.SaveChangesAsync(ct);
        return result;
    }
}
