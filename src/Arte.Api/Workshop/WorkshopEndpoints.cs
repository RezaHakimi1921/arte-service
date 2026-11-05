using System.Text.Json;
using Arte.Api.Cases;
using Arte.Api.Security;
using Arte.Core.Cases;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Workshop;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Workshop;

/// <summary>
/// The workshop's own lists (reported problems and services, intake checklists per motor type) edited in Settings,
/// the checklist ticked on each case, and the problems recorded on a case at any time.
/// </summary>
public static class WorkshopEndpoints
{
    public sealed record OptionInput(string? Kind, string? Group, string? Title, int? SortOrder, bool? IsActive);
    public sealed record ImportOption(string? Kind, string? Group, string? Title);
    public sealed record ImportOptions(ImportOption[]? Items);
    public sealed record ChecklistItemInput(string? Group, string? Title, bool? IsRequired, int? SortOrder, bool? IsActive);
    public sealed record OrderInput(Guid[]? Ids);
    /// <summary>A row ticked at intake. Rows are matched by the template row's id.</summary>
    public sealed record ChecklistTick(Guid? ItemId, bool? IsChecked, string? Note);
    public sealed record TickInput(bool? IsChecked, string? Note);
    public sealed record FindingInput(string? Title, string? Note, string? Source, Guid? OptionId);

    private const int MaxTitle = 80;
    private const int MaxGroup = 40;
    private const int MaxNote = 200;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapWorkshop(this IEndpointRouteBuilder app)
    {
        MapOptions(app);
        MapTemplates(app);
        MapCaseChecklist(app);
        MapFindings(app);
    }

    // ───────── reported problems and services ─────────

    private static void MapOptions(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/intake-options").RequireTenant();

        // Everyone at the desk needs the active list; only the manager sees switched-off rows.
        g.MapGet("/", async (string? kind, bool? includeInactive, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            if (kind is not null && !IntakeOptionKinds.All.Contains(kind))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["kind"] = ["نوع فهرست نامعتبر است."] });
            if (includeInactive == true && !me.RequiredMembership.Has(Permissions.SettingsManage))
                return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

            var q = db.IntakeOptions.AsNoTracking().AsQueryable();
            if (kind is not null) q = q.Where(o => o.Kind == kind);
            if (includeInactive != true) q = q.Where(o => o.IsActive);
            return Results.Ok(await q.OrderBy(o => o.Kind).ThenBy(o => o.SortOrder).ThenBy(o => o.CreatedAt)
                .Select(o => new { o.Id, o.Kind, o.Group, o.Title, o.SortOrder, o.IsActive }).ToListAsync(ct));
        });

        g.MapPost("/", async (OptionInput req, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var errors = ValidateOption(req, isNew: true);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var title = req.Title!.Trim();
            if (await db.IntakeOptions.AnyAsync(o => o.Kind == req.Kind && o.Title == title && o.IsActive, ct))
                return Results.Problem(statusCode: 409, title: "این مورد از قبل در فهرست هست.");

            var next = (await db.IntakeOptions.Where(o => o.Kind == req.Kind).MaxAsync(o => (int?)o.SortOrder, ct) ?? 0) + 10;
            var option = new IntakeOption
            {
                Kind = req.Kind!, Group = NullIfEmpty(req.Group), Title = title, SortOrder = req.SortOrder ?? next, CreatedAt = clock.UtcNow,
            };
            db.IntakeOptions.Add(option);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/intake-options/{option.Id}", new { option.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapPatch("/{id:guid}", async (Guid id, OptionInput req, ArteDbContext db, CancellationToken ct) =>
        {
            var option = await db.IntakeOptions.SingleOrDefaultAsync(o => o.Id == id, ct);
            if (option is null) return Results.NotFound();
            var errors = ValidateOption(req, isNew: false);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            if (req.Title is not null) option.Title = req.Title.Trim();
            if (req.Group is not null) option.Group = NullIfEmpty(req.Group);
            if (req.SortOrder is { } order) option.SortOrder = order;
            if (req.IsActive is { } active) option.IsActive = active;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);

        // «شروع از فهرست پیش‌فرض»: the settings page sends the built-in lists; rows already there (same kind and title) are skipped.
        g.MapPost("/import", async (ImportOptions req, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var items = req.Items ?? [];
            if (items.Length is 0 or > 300)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["items"] = ["۱ تا ۳۰۰ مورد بفرستید."] });
            var errors = new Dictionary<string, string[]>();
            foreach (var i in items)
                foreach (var kv in ValidateOption(new OptionInput(i.Kind, i.Group, i.Title, null, null), isNew: true))
                    errors[kv.Key] = kv.Value;
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var existing = (await db.IntakeOptions.Select(o => new { o.Kind, o.Title }).ToListAsync(ct))
                .Select(o => (o.Kind, o.Title)).ToHashSet();
            var nextByKind = new Dictionary<string, int>();
            foreach (var kind in IntakeOptionKinds.All)
                nextByKind[kind] = (await db.IntakeOptions.Where(o => o.Kind == kind).MaxAsync(o => (int?)o.SortOrder, ct) ?? 0);

            var added = 0;
            foreach (var i in items)
            {
                var title = i.Title!.Trim();
                if (!existing.Add((i.Kind!, title))) continue;
                nextByKind[i.Kind!] += 10;
                db.IntakeOptions.Add(new IntakeOption
                {
                    Kind = i.Kind!, Group = NullIfEmpty(i.Group), Title = title, SortOrder = nextByKind[i.Kind!], CreatedAt = clock.UtcNow,
                });
                added++;
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Added = added });
        }).RequirePermission(Permissions.SettingsManage);
    }

    private static Dictionary<string, string[]> ValidateOption(OptionInput req, bool isNew)
    {
        var e = new Dictionary<string, string[]>();
        if (isNew && (req.Kind is null || !IntakeOptionKinds.All.Contains(req.Kind))) e["kind"] = ["نوع فهرست نامعتبر است."];
        if (!isNew && req.Kind is not null) e["kind"] = ["نوع فهرست قابل تغییر نیست."];
        if (isNew && string.IsNullOrWhiteSpace(req.Title)) e["title"] = ["عنوان لازم است."];
        if (req.Title is { } t && (t.Trim().Length is 0 or > MaxTitle)) e["title"] = [$"عنوان ۱ تا {MaxTitle} حرف."];
        if (req.Group is { Length: > MaxGroup }) e["group"] = [$"دسته حداکثر {MaxGroup} حرف."];
        if (req.SortOrder is < 0 or > 100_000) e["sortOrder"] = ["ترتیب نامعتبر است."];
        return e;
    }

    // ───────── checklist templates (one per motor type) ─────────

    private static void MapTemplates(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/checklist-templates").RequireTenant();

        g.MapGet("/{motorType}", async (string motorType, bool? includeInactive, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            if (!MotorTypes.All.Contains(motorType)) return Results.NotFound();
            if (includeInactive == true && !me.RequiredMembership.Has(Permissions.SettingsManage))
                return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");
            var q = db.ChecklistTemplateItems.AsNoTracking().Where(t => t.MotorType == motorType);
            if (includeInactive != true) q = q.Where(t => t.IsActive);
            return Results.Ok(await q.OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt)
                .Select(t => new { t.Id, t.MotorType, t.Group, t.Title, t.IsRequired, t.SortOrder, t.IsActive }).ToListAsync(ct));
        });

        g.MapPost("/{motorType}/items", async (string motorType, ChecklistItemInput req, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            if (!MotorTypes.All.Contains(motorType)) return Results.NotFound();
            var errors = ValidateTemplateItem(req, isNew: true);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var next = (await db.ChecklistTemplateItems.Where(t => t.MotorType == motorType).MaxAsync(t => (int?)t.SortOrder, ct) ?? 0) + 10;
            var item = new ChecklistTemplateItem
            {
                MotorType = motorType, Group = NullIfEmpty(req.Group), Title = req.Title!.Trim(),
                IsRequired = req.IsRequired ?? true, SortOrder = req.SortOrder ?? next, CreatedAt = clock.UtcNow,
            };
            db.ChecklistTemplateItems.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/checklist-templates/items/{item.Id}", new { item.Id });
        }).RequirePermission(Permissions.SettingsManage);

        g.MapPatch("/items/{id:guid}", async (Guid id, ChecklistItemInput req, ArteDbContext db, CancellationToken ct) =>
        {
            var item = await db.ChecklistTemplateItems.SingleOrDefaultAsync(t => t.Id == id, ct);
            if (item is null) return Results.NotFound();
            var errors = ValidateTemplateItem(req, isNew: false);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            if (req.Title is not null) item.Title = req.Title.Trim();
            if (req.Group is not null) item.Group = NullIfEmpty(req.Group);
            if (req.IsRequired is { } required) item.IsRequired = required;
            if (req.SortOrder is { } order) item.SortOrder = order;
            if (req.IsActive is { } active) item.IsActive = active;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);

        // The new order of the rows, top to bottom. Rows not listed keep their place after the listed ones.
        g.MapPut("/{motorType}/order", async (string motorType, OrderInput req, ArteDbContext db, CancellationToken ct) =>
        {
            if (!MotorTypes.All.Contains(motorType)) return Results.NotFound();
            var ids = req.Ids ?? [];
            if (ids.Length is 0 or > 200 || ids.Distinct().Count() != ids.Length)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["ids"] = ["فهرست ترتیب نامعتبر است."] });
            var rows = await db.ChecklistTemplateItems.Where(t => t.MotorType == motorType).ToListAsync(ct);
            if (ids.Any(id => rows.All(r => r.Id != id))) return Results.NotFound();

            var position = 0;
            foreach (var id in ids) rows.Single(r => r.Id == id).SortOrder = (position += 10);
            foreach (var rest in rows.Where(r => !ids.Contains(r.Id)).OrderBy(r => r.SortOrder)) rest.SortOrder = (position += 10);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);

        // A starting list the manager then edits. Only into an empty list, so nothing they made is ever overwritten.
        g.MapPost("/{motorType}/defaults", async (string motorType, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            if (!MotorTypes.All.Contains(motorType)) return Results.NotFound();
            if (await db.ChecklistTemplateItems.AnyAsync(t => t.MotorType == motorType, ct))
                return Results.Problem(statusCode: 409, title: "این فهرست از قبل مورد دارد؛ فهرست پیش‌فرض فقط در فهرست خالی اضافه می‌شود.");

            var order = 0;
            foreach (var (group, title) in WorkshopDefaults.Checklist(motorType))
                db.ChecklistTemplateItems.Add(new ChecklistTemplateItem
                {
                    MotorType = motorType, Group = group, Title = title, IsRequired = true, SortOrder = (order += 10), CreatedAt = clock.UtcNow,
                });
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { Added = order / 10 });
        }).RequirePermission(Permissions.SettingsManage);
    }

    private static Dictionary<string, string[]> ValidateTemplateItem(ChecklistItemInput req, bool isNew)
    {
        var e = new Dictionary<string, string[]>();
        if (isNew && string.IsNullOrWhiteSpace(req.Title)) e["title"] = ["عنوان لازم است."];
        if (req.Title is { } t && (t.Trim().Length is 0 or > MaxTitle)) e["title"] = [$"عنوان ۱ تا {MaxTitle} حرف."];
        if (req.Group is { Length: > MaxGroup }) e["group"] = [$"دسته حداکثر {MaxGroup} حرف."];
        if (req.SortOrder is < 0 or > 100_000) e["sortOrder"] = ["ترتیب نامعتبر است."];
        return e;
    }

    // ───────── the checklist on a case ─────────

    private static void MapCaseChecklist(IEndpointRouteBuilder app)
    {
        app.MapPut("/api/v1/cases/{id:guid}/checklist/{itemId:guid}", async (Guid id, Guid itemId, TickInput req, RequestUser me,
            ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var m = me.RequiredMembership;
            var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
            if (!CaseAccess.CanWorkOn(m, c) && !m.Has(Permissions.CasesCreate))
                return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");
            var item = await db.CaseChecklistItems.SingleOrDefaultAsync(x => x.Id == itemId && x.CaseId == id, ct);
            if (item is null) return Results.NotFound();
            if (req.Note is { Length: > MaxNote })
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = [$"توضیح حداکثر {MaxNote} حرف."] });

            var now = clock.UtcNow;
            var changed = false;
            if (req.IsChecked is { } isChecked && isChecked != item.IsChecked)
            {
                item.IsChecked = isChecked;
                item.CheckedBy = isChecked ? me.RequiredUserId : null;
                item.CheckedAt = isChecked ? now : null;
                changed = true;
            }
            if (req.Note is not null && NullIfEmpty(req.Note) != item.Note)
            {
                item.Note = NullIfEmpty(req.Note);
                changed = true;
            }
            if (changed)
                AddEvent(db, c, CaseEventTypes.ChecklistChanged, me.RequiredUserId, now, new { item.Title, item.IsChecked, item.Note });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireTenant();
    }

    /// <summary>The case's checklist rows, in the order of the template, with who ticked each.</summary>
    public static async Task<object> ChecklistViewAsync(ArteDbContext db, Guid caseId, CancellationToken ct)
    {
        var rows = await db.CaseChecklistItems.AsNoTracking().Where(x => x.CaseId == caseId).OrderBy(x => x.SortOrder).ToListAsync(ct);
        var names = await NamesAsync(db, rows.Where(r => r.CheckedBy != null).Select(r => r.CheckedBy!.Value), ct);
        return rows.Select(r => new
        {
            r.Id, r.Group, r.Title, r.IsRequired, r.IsChecked, r.Note, r.CheckedAt,
            CheckedByName = r.CheckedBy is { } by ? names.GetValueOrDefault(by) : null,
        }).ToList();
    }

    // ───────── problems recorded on a case ─────────

    private static void MapFindings(IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/cases/{id:guid}/findings").RequireTenant();

        // Recording is allowed at every stage, even after delivery: the data is the point.
        g.MapPost("/", async (Guid id, FindingInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var m = me.RequiredMembership;
            var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
            if (!CaseAccess.CanWorkOn(m, c) && !m.Has(Permissions.CasesCreate))
                return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

            var source = req.Source ?? FindingSources.Mechanic;
            var title = NullIfEmpty(req.Title);
            if (req.OptionId is { } optionId)
            {
                var option = await db.IntakeOptions.AsNoTracking().SingleOrDefaultAsync(o => o.Id == optionId && o.Kind == IntakeOptionKinds.Problem, ct);
                if (option is null)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["optionId"] = ["این مورد در فهرست ایرادها نیست."] });
                title ??= option.Title;
            }
            var errors = new Dictionary<string, string[]>();
            if (!FindingSources.All.Contains(source)) errors["source"] = ["منبع نامعتبر است."];
            if (title is null || title.Length > 120) errors["title"] = ["عنوان ایراد لازم است (حداکثر ۱۲۰ حرف)."];
            if (req.Note is { Length: > 300 }) errors["note"] = ["توضیح حداکثر ۳۰۰ حرف."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var now = clock.UtcNow;
            var finding = new CaseFinding
            {
                CaseId = c.Id, Source = source, Title = title!, Note = NullIfEmpty(req.Note), OptionId = req.OptionId,
                MotorType = c.MotorType, CreatedBy = me.RequiredUserId, CreatedAt = now,
            };
            db.CaseFindings.Add(finding);
            AddEvent(db, c, CaseEventTypes.FindingAdded, me.RequiredUserId, now, new { finding.Title, finding.Source });
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/cases/{id}/findings/{finding.Id}", new { finding.Id });
        });

        g.MapDelete("/{findingId:guid}", async (Guid id, Guid findingId, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var m = me.RequiredMembership;
            var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
            if (!CaseAccess.CanWorkOn(m, c) && !m.Has(Permissions.CasesCreate))
                return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");
            var finding = await db.CaseFindings.SingleOrDefaultAsync(f => f.Id == findingId && f.CaseId == id, ct);
            if (finding is null) return Results.NotFound();

            var now = clock.UtcNow;
            finding.DeletedAt = now;
            finding.DeletedBy = me.RequiredUserId;
            AddEvent(db, c, CaseEventTypes.FindingRemoved, me.RequiredUserId, now, new { finding.Title });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPost("/{findingId:guid}/restore", async (Guid id, Guid findingId, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var m = me.RequiredMembership;
            var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
            if (!CaseAccess.CanWorkOn(m, c) && !m.Has(Permissions.CasesCreate))
                return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");
            var finding = await db.CaseFindings.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter])
                .SingleOrDefaultAsync(f => f.Id == findingId && f.CaseId == id && f.DeletedAt != null, ct);
            if (finding is null) return Results.NotFound();

            finding.DeletedAt = null;
            finding.DeletedBy = null;
            AddEvent(db, c, CaseEventTypes.FindingRestored, me.RequiredUserId, clock.UtcNow, new { finding.Title });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    public static async Task<object> FindingsViewAsync(ArteDbContext db, Guid caseId, CancellationToken ct)
    {
        var rows = await db.CaseFindings.AsNoTracking().Where(f => f.CaseId == caseId).OrderBy(f => f.CreatedAt).ToListAsync(ct);
        var names = await NamesAsync(db, rows.Select(r => r.CreatedBy), ct);
        return rows.Select(f => new { f.Id, f.Source, f.Title, f.Note, f.CreatedAt, CreatedByName = names.GetValueOrDefault(f.CreatedBy) }).ToList();
    }

    // ───────── helpers ─────────

    /// <summary>The name each person has in this branch (the branch's own name, never another branch's).</summary>
    private static async Task<Dictionary<Guid, string>> NamesAsync(ArteDbContext db, IEnumerable<Guid> userIds, CancellationToken ct)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return [];
        return await db.Memberships.IgnoreQueryFilters([ArteDbContext.SupportFilter]).AsNoTracking()
            .Where(m => ids.Contains(m.UserId))
            .Select(m => new { m.UserId, Name = m.DisplayName ?? m.User!.Mobile })
            .ToDictionaryAsync(x => x.UserId, x => x.Name, ct);
    }

    private static void AddEvent(ArteDbContext db, Case c, string type, Guid actor, DateTimeOffset at, object data) =>
        db.CaseEvents.Add(new CaseEvent
        {
            TenantId = c.TenantId, CaseId = c.Id, Type = type, ActorUserId = actor, OccurredAt = at,
            Data = JsonSerializer.SerializeToDocument(data, Json),
        });

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
