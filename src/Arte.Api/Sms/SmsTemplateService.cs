using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Sms;

public sealed record SmsTemplateView(string Key, string Title, string DefaultText, string? DraftText, string? ApprovedText,
    string Status, DateTimeOffset? ApprovedAt, bool NeedsLink, string? PendingRequestId = null);

public sealed record SmsTemplateRequestView(
    Guid Id, Guid TenantId, string TenantName, string MessageKey, string Title, string ProposedText,
    string? Note, string Status, DateTimeOffset CreatedAt, string? ReviewNote);

/// <summary>
/// A branch's own wording of each message: draft → approved by the platform admin → sent. Anything not approved
/// falls back to the system text (or is refused, when the server requires approval).
/// Shops cannot save text directly; they open a change request the platform admin reviews.
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
        var pending = await db.SmsTemplateChangeRequests.AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.Status == SmsTemplateRequestStates.Pending)
            .Select(r => new { r.Id, r.MessageKey }).ToListAsync(ct);
        return SmsTemplates.Branch.Select(d =>
        {
            var r = rows.FirstOrDefault(x => x.MessageKey == d.Key);
            var status = r?.ApprovedText is not null && (r.DraftText is null || r.DraftText == r.ApprovedText) ? StatusApproved
                : r?.DraftText is not null ? StatusDraft : StatusDefault;
            var reqId = pending.FirstOrDefault(p => p.MessageKey == d.Key)?.Id;
            return new SmsTemplateView(d.Key, d.Title, d.Default, r?.DraftText, r?.ApprovedText, status, r?.ApprovedAt, d.NeedsLink,
                reqId?.ToString());
        }).ToList();
    }

    /// <summary>Shop asks Arte to change a template. Replaces any earlier pending request for the same key.</summary>
    public async Task<(SmsTemplateChangeRequest? Request, string? Error)> RequestChangeAsync(
        Guid tenantId, string key, string proposed, string? note, Guid by, CancellationToken ct)
    {
        proposed = proposed.Trim();
        if (SmsTemplates.Validate(key, proposed) is { } error) return (null, error);
        if (note is { Length: > 500 }) return (null, "توضیح حداکثر ۵۰۰ حرف است.");
        var old = await db.SmsTemplateChangeRequests
            .Where(r => r.TenantId == tenantId && r.MessageKey == key && r.Status == SmsTemplateRequestStates.Pending)
            .ToListAsync(ct);
        foreach (var o in old) o.Status = SmsTemplateRequestStates.Rejected;
        var row = new SmsTemplateChangeRequest
        {
            TenantId = tenantId, MessageKey = key, ProposedText = proposed, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            Status = SmsTemplateRequestStates.Pending, RequestedBy = by, CreatedAt = clock.UtcNow,
        };
        db.SmsTemplateChangeRequests.Add(row);
        await db.SaveChangesAsync(ct);
        return (row, null);
    }

    public async Task<int> PendingRequestCountAsync(CancellationToken ct) =>
        await db.SmsTemplateChangeRequests.AsNoTracking().CountAsync(r => r.Status == SmsTemplateRequestStates.Pending, ct);

    public async Task<IReadOnlyList<SmsTemplateRequestView>> ListRequestsAsync(string? status, CancellationToken ct)
    {
        var q = db.SmsTemplateChangeRequests.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(r => r.Status == status);
        else q = q.Where(r => r.Status == SmsTemplateRequestStates.Pending);
        var rows = await q.OrderByDescending(r => r.CreatedAt).Take(100)
            .Join(db.Tenants.AsNoTracking(), r => r.TenantId, t => t.Id, (r, t) => new { r, t.Name })
            .ToListAsync(ct);
        return rows.Select(x => new SmsTemplateRequestView(
            x.r.Id, x.r.TenantId, x.Name, x.r.MessageKey, SmsTemplates.Find(x.r.MessageKey)?.Title ?? x.r.MessageKey,
            x.r.ProposedText, x.r.Note, x.r.Status, x.r.CreatedAt, x.r.ReviewNote)).ToList();
    }

    public async Task<string?> ApproveRequestAsync(Guid requestId, Guid by, CancellationToken ct)
    {
        var req = await db.SmsTemplateChangeRequests.SingleOrDefaultAsync(r => r.Id == requestId, ct);
        if (req is null) return "درخواست پیدا نشد.";
        if (req.Status != SmsTemplateRequestStates.Pending) return "این درخواست قبلاً بررسی شده است.";
        if (SmsTemplates.Validate(req.MessageKey, req.ProposedText) is { } error) return error;
        var row = await EnsureAsync(req.TenantId, req.MessageKey, ct);
        row.DraftText = req.ProposedText;
        row.ApprovedText = req.ProposedText;
        row.ApprovedAt = clock.UtcNow;
        row.ApprovedBy = by;
        row.UpdatedAt = row.ApprovedAt.Value;
        req.Status = SmsTemplateRequestStates.Approved;
        req.ReviewedBy = by;
        req.ReviewedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> RejectRequestAsync(Guid requestId, Guid by, string? reviewNote, CancellationToken ct)
    {
        var req = await db.SmsTemplateChangeRequests.SingleOrDefaultAsync(r => r.Id == requestId, ct);
        if (req is null) return "درخواست پیدا نشد.";
        if (req.Status != SmsTemplateRequestStates.Pending) return "این درخواست قبلاً بررسی شده است.";
        req.Status = SmsTemplateRequestStates.Rejected;
        req.ReviewedBy = by;
        req.ReviewedAt = clock.UtcNow;
        req.ReviewNote = string.IsNullOrWhiteSpace(reviewNote) ? null : reviewNote.Trim();
        await db.SaveChangesAsync(ct);
        return null;
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
