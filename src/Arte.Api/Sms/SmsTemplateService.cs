using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Sms;

public sealed record SmsTemplateView(string Key, string Title, string DefaultText, string? DraftText, string? ApprovedText,
    string Status, DateTimeOffset? ApprovedAt, bool NeedsLink);

/// <summary>
/// A branch's own wording of each message: draft → approved by the platform admin → sent. Anything not approved
/// falls back to the system text (or is refused, when the server requires approval).
/// </summary>
public sealed class SmsTemplateService(ArteDbContext db, IClock clock)
{
    public const string StatusDefault = "default";     // no text of its own: system text
    public const string StatusDraft = "draft";         // a draft that is not approved (yet)
    public const string StatusApproved = "approved";

    /// <summary>The text to send: the branch's approved one, else null.</summary>
    public async Task<string?> ApprovedTextAsync(Guid tenantId, string key, CancellationToken ct) =>
        await db.SmsTemplates.AsNoTracking().Where(t => t.TenantId == tenantId && t.MessageKey == key)
            .Select(t => t.ApprovedText).SingleOrDefaultAsync(ct);

    public async Task<IReadOnlyList<SmsTemplateView>> ListAsync(Guid tenantId, CancellationToken ct)
    {
        var rows = await db.SmsTemplates.AsNoTracking().Where(t => t.TenantId == tenantId).ToListAsync(ct);
        return SmsTemplates.Branch.Select(d =>
        {
            var r = rows.FirstOrDefault(x => x.MessageKey == d.Key);
            var status = r?.ApprovedText is not null && (r.DraftText is null || r.DraftText == r.ApprovedText) ? StatusApproved
                : r?.DraftText is not null ? StatusDraft : StatusDefault;
            return new SmsTemplateView(d.Key, d.Title, d.Default, r?.DraftText, r?.ApprovedText, status, r?.ApprovedAt, d.NeedsLink);
        }).ToList();
    }

    /// <summary>Saves the admin's draft. Returns a Persian error, or null.</summary>
    public async Task<string?> SaveDraftAsync(Guid tenantId, string key, string text, CancellationToken ct)
    {
        text = text.Trim();
        if (SmsTemplates.Validate(key, text) is { } error) return error;
        var row = await EnsureAsync(tenantId, key, ct);
        row.DraftText = text;
        row.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> ApproveAsync(Guid tenantId, string key, Guid by, CancellationToken ct)
    {
        var row = await db.SmsTemplates.SingleOrDefaultAsync(t => t.TenantId == tenantId && t.MessageKey == key, ct);
        if (row?.DraftText is null) return "برای این پیام متنی نوشته نشده است.";
        if (SmsTemplates.Validate(key, row.DraftText) is { } error) return error;
        row.ApprovedText = row.DraftText;
        row.ApprovedAt = clock.UtcNow;
        row.ApprovedBy = by;
        row.UpdatedAt = row.ApprovedAt.Value;
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Back to the system text: draft and approved text are cleared.</summary>
    public async Task ResetAsync(Guid tenantId, string key, CancellationToken ct)
    {
        var row = await db.SmsTemplates.SingleOrDefaultAsync(t => t.TenantId == tenantId && t.MessageKey == key, ct);
        if (row is null) return;
        row.DraftText = null;
        row.ApprovedText = null;
        row.ApprovedAt = null;
        row.ApprovedBy = null;
        row.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task<SmsTemplate> EnsureAsync(Guid tenantId, string key, CancellationToken ct)
    {
        var row = await db.SmsTemplates.SingleOrDefaultAsync(t => t.TenantId == tenantId && t.MessageKey == key, ct);
        if (row is not null) return row;
        row = new SmsTemplate { TenantId = tenantId, MessageKey = key, UpdatedAt = clock.UtcNow };
        db.SmsTemplates.Add(row);
        return row;
    }

    /// <summary>The tag values of one message, from the tokens the callers already build.</summary>
    public static Dictionary<string, string> Values(string messageKey, IReadOnlyDictionary<string, string> tokens,
        IReadOnlyDictionary<string, string>? extra, string linkBase)
    {
        string T(string k) => tokens.GetValueOrDefault(k, "");
        var code = T("code");
        var path = messageKey == MessageKeys.SurveyRequest ? "s" : "t";
        return new Dictionary<string, string>
        {
            [SmsTags.CustomerName] = T("name"),
            [SmsTags.WorkshopName] = T("shop"),
            [SmsTags.Vehicle] = T("vehicle"),
            [SmsTags.Code] = code,
            [SmsTags.CustomerLink] = code.Length == 0 ? "" : $"{linkBase.TrimEnd('/')}/{path}/{code}",
            [SmsTags.CaseCode] = extra?.GetValueOrDefault("CaseCode") ?? (messageKey == MessageKeys.StaffWorkFinished ? code : ""),
        };
    }
}
