using System.Text.Json;
using Arte.Api.Security;
using Arte.Core.Billing;
using Arte.Core.Cases;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Workflows;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Cases;

public static class DamageEndpoints
{
    private const int MaxDamageImages = 3;
    private const int MaxDamageVideos = 1;
    private const long MaxRials = 1_000_000_000_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record DamageInput(long? AmountRials, string? InternalNote, bool? ShowReportToCustomer, bool? ShowMediaToCustomer);
    public sealed record VoidInput(string? Reason);
    public sealed record ShopLossInput(long? AmountRials, string? InternalNote, Guid? ResponsibleUserId, Guid? CaseId);
    public sealed record ReturnInput(string? Reason);

    public static void MapDamage(this IEndpointRouteBuilder app)
    {
        var damages = app.MapGroup("/api/v1/cases/{caseId:guid}/customer-damages").RequireTenant();
        damages.MapGet("/", ListDamagesAsync);
        damages.MapPost("/", CreateDamageAsync).RequirePermission(Permissions.CasesCreate);
        damages.MapPatch("/{reportId:guid}", UpdateDamageAsync).RequirePermission(Permissions.CasesCreate);
        // POST (not DELETE): void needs a JSON reason body; DELETE cannot take an inferred body in minimal APIs.
        damages.MapPost("/{reportId:guid}/void", VoidDamageAsync).RequirePermission(Permissions.CasesCreate);
        damages.MapPost("/{reportId:guid}/restore", RestoreDamageAsync).RequirePermission(Permissions.CasesCreate);
        damages.MapPost("/{reportId:guid}/photos", UploadDamagePhotoAsync).RequirePermission(Permissions.CasesCreate);

        app.MapPost("/api/v1/cases/{caseId:guid}/mark-returned", MarkReturnedAsync).RequireTenant().RequirePermission(Permissions.CasesCreate);

        // Case-scoped list for the damage module on the case page (same visibility as customer damages).
        app.MapGet("/api/v1/cases/{caseId:guid}/shop-losses", ListCaseShopLossesAsync).RequireTenant();

        var losses = app.MapGroup("/api/v1/shop-losses").RequireTenant();
        losses.MapGet("/", ListShopLossesAsync).RequirePermission(Permissions.ReportsView);
        losses.MapPost("/", CreateShopLossAsync).RequirePermission(Permissions.CasesCreate);
        losses.MapPatch("/{id:guid}", UpdateShopLossAsync).RequirePermission(Permissions.CasesCreate);
        losses.MapDelete("/{id:guid}", DeleteShopLossAsync).RequirePermission(Permissions.CasesCreate);
        losses.MapPost("/{id:guid}/restore", RestoreShopLossAsync).RequirePermission(Permissions.CasesCreate);
    }

    private static bool CanWriteDamage(Membership m, Case c) => m.Has(Permissions.CasesCreate) && CaseAccess.CanSee(m, c);

    private static void AddEvent(ArteDbContext db, Case c, string type, Guid actor, DateTimeOffset at, object data) =>
        db.CaseEvents.Add(new CaseEvent
        {
            TenantId = c.TenantId, CaseId = c.Id, Type = type, ActorUserId = actor, OccurredAt = at,
            Data = JsonSerializer.SerializeToDocument(data, Json),
        });

    private static async Task<List<CaseItem>> LinkedItemsAsync(ArteDbContext db, Guid damageId, Guid? excludeItemId, CancellationToken ct) =>
        await db.CaseItems.AsNoTracking()
            .Where(i => i.CustomerDamageId == damageId && (excludeItemId == null || i.Id != excludeItemId))
            .ToListAsync(ct);

    internal static long RemainingAsync(long amount, IEnumerable<CaseItem> linked) =>
        DamageCompensation.RemainingCreditRials(amount, linked);

    private static object DamageView(CustomerDamageReport r, long remaining, IEnumerable<object>? media = null) => new
    {
        r.Id, r.CaseId, r.AmountRials, RemainingRials = remaining, r.InternalNote,
        r.ShowReportToCustomer, r.ShowMediaToCustomer, r.CreatedAt, r.UpdatedAt,
        r.VoidedAt, r.VoidReason, Media = media,
    };

    private static async Task SyncDamageMediaVisibility(ArteDbContext db, CustomerDamageReport report, CancellationToken ct)
    {
        var visible = report.ShowReportToCustomer && report.ShowMediaToCustomer;
        await db.CaseAttachments.Where(a => a.DamageReportId == report.Id && a.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.VisibleToCustomer, visible), ct);
    }

    private static async Task<IResult> ListDamagesAsync(Guid caseId, RequestUser me, ArteDbContext db, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();

        var reports = await db.CustomerDamageReports.AsNoTracking().Where(x => x.CaseId == caseId && x.VoidedAt == null)
            .OrderBy(x => x.CreatedAt).ToListAsync(ct);
        var result = new List<object>();
        foreach (var r in reports)
        {
            var linked = await LinkedItemsAsync(db, r.Id, null, ct);
            var media = await db.CaseAttachments.AsNoTracking().Where(a => a.DamageReportId == r.Id && a.DeletedAt == null)
                .OrderBy(a => a.CreatedAt).Select(a => new { a.Id, IsVideo = a.ContentType.StartsWith("video/") }).ToListAsync(ct);
            result.Add(DamageView(r, RemainingAsync(r.AmountRials, linked), media));
        }
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateDamageAsync(Guid caseId, DamageInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
        if (!CanWriteDamage(m, c)) return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

        var errors = ValidateDamage(req, isNew: true);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var now = clock.UtcNow;
        var report = new CustomerDamageReport
        {
            CaseId = c.Id,
            AmountRials = req.AmountRials!.Value,
            InternalNote = string.IsNullOrWhiteSpace(req.InternalNote) ? null : req.InternalNote.Trim(),
            ShowReportToCustomer = req.ShowReportToCustomer ?? false,
            ShowMediaToCustomer = req.ShowMediaToCustomer ?? false,
            CreatedBy = me.RequiredUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.CustomerDamageReports.Add(report);
        AddEvent(db, c, CaseEventTypes.CustomerDamageCreated, me.RequiredUserId, now,
            new { report.AmountRials, report.ShowReportToCustomer, report.ShowMediaToCustomer });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/cases/{caseId}/customer-damages/{report.Id}",
            DamageView(report, report.AmountRials, []));
    }

    private static async Task<IResult> UpdateDamageAsync(Guid caseId, Guid reportId, DamageInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
        if (!CanWriteDamage(m, c)) return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

        var report = await db.CustomerDamageReports.SingleOrDefaultAsync(x => x.Id == reportId && x.CaseId == caseId && x.VoidedAt == null, ct);
        if (report is null) return Results.NotFound();

        var errors = ValidateDamage(req, isNew: false);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        if (req.AmountRials is { } amount)
        {
            var linked = await db.CaseItems.Where(i => i.CustomerDamageId == reportId).ToListAsync(ct);
            var applied = linked.Sum(DamageCompensation.AppliedCreditRials);
            if (amount < applied)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["amountRials"] = ["مبلغ از جبران‌شده کمتر است."] });
            report.AmountRials = amount;
        }
        if (req.InternalNote is not null)
            report.InternalNote = string.IsNullOrWhiteSpace(req.InternalNote) ? null : req.InternalNote.Trim();
        if (req.ShowReportToCustomer is { } showReport) report.ShowReportToCustomer = showReport;
        if (req.ShowMediaToCustomer is { } showMedia) report.ShowMediaToCustomer = showMedia;
        report.UpdatedAt = clock.UtcNow;

        await SyncDamageMediaVisibility(db, report, ct);
        AddEvent(db, c, CaseEventTypes.CustomerDamageUpdated, me.RequiredUserId, report.UpdatedAt, new { report.AmountRials });
        await db.SaveChangesAsync(ct);

        var linkedView = await LinkedItemsAsync(db, report.Id, null, ct);
        return Results.Ok(DamageView(report, RemainingAsync(report.AmountRials, linkedView)));
    }

    private static async Task<IResult> VoidDamageAsync(Guid caseId, Guid reportId, VoidInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
        if (!CanWriteDamage(m, c)) return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

        var report = await db.CustomerDamageReports.SingleOrDefaultAsync(x => x.Id == reportId && x.CaseId == caseId && x.VoidedAt == null, ct);
        if (report is null) return Results.NotFound();
        var reason = req.Reason?.Trim() ?? "";
        if (reason.Length is 0 or > 500)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["علت ابطال لازم است (حداکثر ۵۰۰ حرف)."] });

        var now = clock.UtcNow;
        report.VoidedAt = now;
        report.VoidedBy = me.RequiredUserId;
        report.VoidReason = reason;
        report.UpdatedAt = now;
        AddEvent(db, c, CaseEventTypes.CustomerDamageVoided, me.RequiredUserId, now, new { report.AmountRials, Reason = reason });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreDamageAsync(Guid caseId, Guid reportId, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
        if (!CanWriteDamage(m, c)) return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

        var report = await db.CustomerDamageReports.SingleOrDefaultAsync(x => x.Id == reportId && x.CaseId == caseId && x.VoidedAt != null, ct);
        if (report is null) return Results.NotFound();
        report.VoidedAt = null;
        report.VoidedBy = null;
        report.VoidReason = null;
        report.UpdatedAt = clock.UtcNow;
        AddEvent(db, c, CaseEventTypes.CustomerDamageRestored, me.RequiredUserId, report.UpdatedAt, new { report.AmountRials });
        await db.SaveChangesAsync(ct);
        return Results.Ok(DamageView(report, report.AmountRials));
    }

    private static async Task<IResult> UploadDamagePhotoAsync(Guid caseId, Guid reportId, HttpRequest request, RequestUser me,
        ArteDbContext db, IClock clock, IConfiguration config, IHostEnvironment env, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
        if (!CanWriteDamage(m, c)) return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

        var report = await db.CustomerDamageReports.SingleOrDefaultAsync(x => x.Id == reportId && x.CaseId == caseId && x.VoidedAt == null, ct);
        if (report is null) return Results.NotFound();

        var existing = await db.CaseAttachments.AsNoTracking().Where(a => a.DamageReportId == reportId && a.DeletedAt == null).ToListAsync(ct);
        var imageCount = existing.Count(a => !a.ContentType.StartsWith("video/"));
        var videoCount = existing.Count(a => a.ContentType.StartsWith("video/"));

        var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = AttachmentEndpoints.MaxVideoBytes + 64 * 1024;
        if (!request.HasFormContentType) return Results.Problem(statusCode: 400, title: "فایلی ارسال نشده است.");

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return Results.Problem(statusCode: 400, title: "فایلی ارسال نشده است.");
        if (file.Length > AttachmentEndpoints.MaxVideoBytes) return Results.Problem(statusCode: 400, title: "حجم فیلم حداکثر ۲۰۰ مگابایت است.");

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var image = AttachmentEndpoints.Sniff(bytes);
        var video = image is null ? AttachmentEndpoints.SniffVideo(bytes) : null;
        if (image is null && video is null)
            return Results.Problem(statusCode: 400, title: "فقط عکس JPG، PNG یا WebP (یا فیلم MP4/MOV/WebM) قابل بارگذاری است.");
        if (image is not null && bytes.Length > AttachmentEndpoints.MaxBytes)
            return Results.Problem(statusCode: 400, title: "حجم عکس حداکثر ۶ مگابایت است.");
        if (image is not null && imageCount >= MaxDamageImages)
            return Results.Problem(statusCode: 400, title: "حداکثر ۳ عکس برای هر گزارش خسارت.");
        if (video is not null && videoCount >= MaxDamageVideos)
            return Results.Problem(statusCode: 400, title: "حداکثر ۱ فیلم برای هر گزارش خسارت.");

        var kind = image ?? video!.Value;
        var stageKey = await db.Stages.Where(s => s.Id == c.StageId).Select(s => s.Key).SingleAsync(ct);
        var id = Guid.CreateVersion7();
        var relative = Path.Combine(c.TenantId.ToString("N"), c.Id.ToString("N"), $"{id:N}.{kind.Ext}");
        var full = Path.Combine(AttachmentEndpoints.Root(config, env), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, bytes, ct);

        var visible = report.ShowReportToCustomer && report.ShowMediaToCustomer;
        var attachment = new CaseAttachment
        {
            Id = id, CaseId = c.Id, StageKey = stageKey, ContentType = kind.Type, SizeBytes = bytes.Length,
            StoragePath = relative.Replace('\\', '/'), Purpose = AttachmentPurposes.Photo,
            DamageReportId = reportId, VisibleToCustomer = visible,
            CreatedBy = me.RequiredUserId, CreatedAt = clock.UtcNow,
        };
        db.CaseAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/attachments/{id}", new { attachment.Id, attachment.VisibleToCustomer, IsVideo = video is not null });
    }

    private static async Task<IResult> MarkReturnedAsync(Guid caseId, ReturnInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
        if (!m.Has(Permissions.CasesCreate)) return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

        var stageKey = await db.Stages.AsNoTracking().Where(s => s.Id == c.StageId).Select(s => s.Key).SingleAsync(ct);
        if (stageKey != "delivered")
            return Results.Problem(statusCode: 409, title: "نشانگر برگشتی فقط روی پروندهٔ تحویل‌شده است.");

        var reason = req.Reason?.Trim() ?? "";
        if (reason.Length is 0 or > 500)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["دلیل برگشتی لازم است (حداکثر ۵۰۰ حرف)."] });

        var now = clock.UtcNow;
        var priorStageId = c.StageId;
        c.ReturnMarkedAt = now;
        c.ReturnReason = reason;
        AddEvent(db, c, CaseEventTypes.ReturnMarked, me.RequiredUserId, now, new { Reason = reason });
        await db.SaveChangesAsync(ct);
        return Results.Ok(new { c.ReturnMarkedAt, c.ReturnReason, StageId = c.StageId, PriorStageId = priorStageId });
    }

    private static Dictionary<string, string[]> ValidateDamage(DamageInput req, bool isNew)
    {
        var e = new Dictionary<string, string[]>();
        if (isNew && (req.AmountRials is not > 0 or > MaxRials)) e["amountRials"] = ["مبلغ بستانکاری لازم است."];
        if (!isNew && req.AmountRials is not null and (<= 0 or > MaxRials)) e["amountRials"] = ["مبلغ نامعتبر است."];
        if (req.InternalNote is { Length: > 500 }) e["internalNote"] = ["حداکثر ۵۰۰ حرف."];
        return e;
    }

    // ───────── shop losses ─────────

    private static async Task<IResult> ListCaseShopLossesAsync(Guid caseId, RequestUser me, ArteDbContext db, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();

        var rows = await (
            from x in db.ShopLossReports.AsNoTracking()
            where x.CaseId == caseId
            join mem in db.Memberships.IgnoreQueryFilters([ArteDbContext.SupportFilter]).AsNoTracking()
                on x.ResponsibleUserId equals mem.Id into mj
            from mem in mj.DefaultIfEmpty()
            orderby x.CreatedAt descending
            select new
            {
                x.Id, x.CaseId, x.AmountRials, x.InternalNote, x.ResponsibleUserId, x.CreatedAt, x.UpdatedAt,
                ResponsibleName = mem == null ? null : (mem.DisplayName ?? mem.User!.Mobile),
            }).ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> ListShopLossesAsync(RequestUser me, ArteDbContext db, DateTimeOffset? from, DateTimeOffset? to, Guid? caseId, CancellationToken ct)
    {
        var q = db.ShopLossReports.AsNoTracking().AsQueryable();
        if (from is { } f) q = q.Where(x => x.CreatedAt >= f);
        if (to is { } t) q = q.Where(x => x.CreatedAt < t);
        if (caseId is { } cid) q = q.Where(x => x.CaseId == cid);
        var rows = await q.OrderByDescending(x => x.CreatedAt).Take(200).Select(x => new
        {
            x.Id, x.CaseId, x.AmountRials, x.InternalNote, x.ResponsibleUserId, x.CreatedAt, x.UpdatedAt,
        }).ToListAsync(ct);
        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateShopLossAsync(ShopLossInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var errors = ValidateShopLoss(req, isNew: true);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        if (req.CaseId is { } cid)
        {
            var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == cid, ct);
            if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
        }
        if (!await db.Memberships.AnyAsync(x => x.Id == req.ResponsibleUserId && x.IsActive, ct))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["responsibleUserId"] = ["مسئول پیدا نشد."] });

        var now = clock.UtcNow;
        var row = new ShopLossReport
        {
            CaseId = req.CaseId,
            AmountRials = req.AmountRials!.Value,
            InternalNote = string.IsNullOrWhiteSpace(req.InternalNote) ? null : req.InternalNote.Trim(),
            ResponsibleUserId = req.ResponsibleUserId!.Value,
            CreatedBy = me.RequiredUserId,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.ShopLossReports.Add(row);
        if (req.CaseId is { } caseId)
        {
            var c = await db.Cases.SingleAsync(x => x.Id == caseId, ct);
            AddEvent(db, c, CaseEventTypes.ShopLossCreated, me.RequiredUserId, now, new { row.AmountRials, row.ResponsibleUserId });
        }
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/shop-losses/{row.Id}", row);
    }

    private static async Task<IResult> UpdateShopLossAsync(Guid id, ShopLossInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var row = await db.ShopLossReports.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return Results.NotFound();
        var errors = ValidateShopLoss(req, isNew: false);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        if (req.AmountRials is { } amount) row.AmountRials = amount;
        if (req.InternalNote is not null) row.InternalNote = string.IsNullOrWhiteSpace(req.InternalNote) ? null : req.InternalNote.Trim();
        if (req.ResponsibleUserId is { } resp)
        {
            if (!await db.Memberships.AnyAsync(x => x.Id == resp && x.IsActive, ct))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["responsibleUserId"] = ["مسئول پیدا نشد."] });
            row.ResponsibleUserId = resp;
        }
        row.UpdatedAt = clock.UtcNow;
        if (row.CaseId is { } caseId)
        {
            var c = await db.Cases.SingleAsync(x => x.Id == caseId, ct);
            if (!CaseAccess.CanSee(me.RequiredMembership, c)) return Results.NotFound();
            AddEvent(db, c, CaseEventTypes.ShopLossUpdated, me.RequiredUserId, row.UpdatedAt, new { row.AmountRials });
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(row);
    }

    private static async Task<IResult> DeleteShopLossAsync(Guid id, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var row = await db.ShopLossReports.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return Results.NotFound();
        row.DeletedAt = clock.UtcNow;
        row.DeletedBy = me.RequiredUserId;
        if (row.CaseId is { } caseId)
        {
            var c = await db.Cases.SingleAsync(x => x.Id == caseId, ct);
            AddEvent(db, c, CaseEventTypes.ShopLossRemoved, me.RequiredUserId, row.DeletedAt.Value, new { row.AmountRials });
        }
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RestoreShopLossAsync(Guid id, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var row = await db.ShopLossReports.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter])
            .SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt != null, ct);
        if (row is null) return Results.NotFound();
        row.DeletedAt = null;
        row.DeletedBy = null;
        row.UpdatedAt = clock.UtcNow;
        if (row.CaseId is { } caseId)
        {
            var c = await db.Cases.SingleAsync(x => x.Id == caseId, ct);
            AddEvent(db, c, CaseEventTypes.ShopLossRestored, me.RequiredUserId, row.UpdatedAt, new { row.AmountRials });
        }
        await db.SaveChangesAsync(ct);
        return Results.Ok(row);
    }

    private static Dictionary<string, string[]> ValidateShopLoss(ShopLossInput req, bool isNew)
    {
        var e = new Dictionary<string, string[]>();
        if (isNew && (req.AmountRials is not > 0 or > MaxRials)) e["amountRials"] = ["مبلغ لازم است."];
        if (!isNew && req.AmountRials is not null and (<= 0 or > MaxRials)) e["amountRials"] = ["مبلغ نامعتبر است."];
        if (isNew && req.ResponsibleUserId is null) e["responsibleUserId"] = ["مسئول لازم است."];
        if (req.InternalNote is { Length: > 500 }) e["internalNote"] = ["حداکثر ۵۰۰ حرف."];
        return e;
    }

    /// <summary>Apply compensation discount on a case item; returns validation problem or null.</summary>
    internal static async Task<IResult?> ApplyCompensationAsync(
        ArteDbContext db, Case c, CaseItem item, Guid? customerDamageId, string? excessMode,
        Membership m, Guid userId, DateTimeOffset now, CancellationToken ct)
    {
        if (customerDamageId is null)
        {
            if (item.CompensationShopLossId is { } oldLossId)
            {
                var old = await db.ShopLossReports.SingleOrDefaultAsync(x => x.Id == oldLossId, ct);
                if (old is not null && old.DeletedAt is null)
                {
                    old.DeletedAt = now;
                    old.DeletedBy = userId;
                    old.UpdatedAt = now;
                }
            }
            DamageCompensation.ClearCompensation(item);
            return null;
        }

        if (!m.Has(Permissions.CasesCreate))
            return Results.Problem(statusCode: 403, title: "جبران خسارت فقط با دسترسی ثبت پرونده است.");

        var report = await db.CustomerDamageReports.SingleOrDefaultAsync(
            x => x.Id == customerDamageId && x.CaseId == c.Id && x.VoidedAt == null, ct);
        if (report is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["customerDamageId"] = ["گزارش خسارت پیدا نشد."] });

        if (item.Kind != ItemKinds.Part || item.Supplier != Suppliers.Shop)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["customerDamageId"] = ["جبران فقط روی قطعهٔ مغازه است."] });

        item.CustomerDamageId = customerDamageId;

        if (item.Status != ItemStatuses.Used)
        {
            item.DiscountRials = 0;
            item.CompensationCreditRials = 0;
            item.CompensationExcessMode = null;
            return null;
        }

        var mode = excessMode ?? CompensationExcessModes.CustomerPurchase;
        if (!CompensationExcessModes.All.Contains(mode))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["compensationExcessMode"] = ["حالت مابه‌التفاوت نامعتبر است."] });

        var linked = await db.CaseItems.Where(i => i.CustomerDamageId == customerDamageId && i.Id != item.Id).ToListAsync(ct);
        var remaining = RemainingAsync(report.AmountRials, linked);
        var gross = DamageCompensation.LineGrossRials(item);
        if (gross > remaining && excessMode is null)
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["compensationExcessMode"] = ["قیمت قطعه از ماندهٔ بستانکاری بیشتر است؛ مسیر مابه‌التفاوت را انتخاب کنید."]
            });

        DamageCompensation.ApplyToItem(item, remaining, mode, out var shopLossExcess);

        if (shopLossExcess > 0 && mode == CompensationExcessModes.ShopLoss)
        {
            ShopLossReport loss;
            if (item.CompensationShopLossId is { } lossId)
            {
                loss = await db.ShopLossReports.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter])
                    .SingleAsync(x => x.Id == lossId, ct);
                loss.DeletedAt = null;
                loss.DeletedBy = null;
                loss.AmountRials = shopLossExcess;
                loss.UpdatedAt = now;
                loss.InternalNote = $"مابه‌التفاوت جبران قطعه «{item.Title}»";
            }
            else
            {
                loss = new ShopLossReport
                {
                    CaseId = c.Id,
                    AmountRials = shopLossExcess,
                    InternalNote = $"مابه‌التفاوت جبران قطعه «{item.Title}»",
                    ResponsibleUserId = item.PerformedBy ?? m.Id,
                    CreatedBy = userId,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                db.ShopLossReports.Add(loss);
                item.CompensationShopLossId = loss.Id;
                AddEvent(db, c, CaseEventTypes.ShopLossCreated, userId, now, new { loss.AmountRials, loss.ResponsibleUserId, ItemId = item.Id });
            }
        }
        else if (item.CompensationShopLossId is { } unusedLossId)
        {
            var unused = await db.ShopLossReports.SingleOrDefaultAsync(x => x.Id == unusedLossId, ct);
            if (unused is not null && unused.DeletedAt is null)
            {
                unused.DeletedAt = now;
                unused.DeletedBy = userId;
                unused.UpdatedAt = now;
            }
            item.CompensationShopLossId = null;
        }

        return null;
    }
}