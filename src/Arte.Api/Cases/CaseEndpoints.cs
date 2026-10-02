using System.Text.Json;
using Arte.Api.Security;
using Arte.Core.Cases;
using Arte.Core.Common;
using Arte.Core.Customers;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Cases;

public static class CaseEndpoints
{
    public sealed record NewAsset(string? Title, string? Identifier);

    public sealed record CreateCase(
        string? Mobile, string? CustomerName, Guid? AssetId, NewAsset? NewAsset,
        string? Request, int? OdometerKm, Guid? AssigneeId, Guid? ParentCaseId, string? Relation,
        Dictionary<string, string>? Intake, DateTimeOffset? PromisedAt, long? EstimatedAmountRials);

    public sealed record UpdateCase(
        string? Request, string? Diagnosis, int? OdometerKm, long? EstimatedAmountRials,
        DateTimeOffset? PromisedAt, string? CustodyStatus, Dictionary<string, string>? Intake);

    public sealed record RunTransition(string? Reason);
    public sealed record Assign(Guid? AssigneeId);
    public sealed record Note(string? Text);

    private const int StuckAfterDays = 2;

    public static void MapCases(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/cases").RequireTenant();

        g.MapGet("/", ListAsync);
        g.MapGet("/{id:guid}", DetailAsync);
        g.MapPost("/", CreateAsync).RequirePermission(Permissions.CasesCreate);
        g.MapPatch("/{id:guid}", UpdateAsync);
        g.MapPost("/{id:guid}/transitions/{transitionId:guid}", TransitionAsync);
        g.MapPost("/{id:guid}/assign", AssignAsync).RequirePermission(Permissions.CasesAssign);
        g.MapPost("/{id:guid}/notes", NoteAsync);
        g.MapDelete("/{id:guid}", DeleteAsync).RequirePermission(Permissions.CasesCreate);
        g.MapPost("/{id:guid}/restore", RestoreAsync).RequirePermission(Permissions.CasesCreate);
        g.MapGet("/trash", TrashAsync).RequirePermission(Permissions.CasesCreate);
        g.MapGet("/suggest-parent", SuggestParentAsync).RequirePermission(Permissions.CasesCreate);

        app.MapGet("/api/v1/dashboard", DashboardAsync).RequireTenant();
        app.MapGet("/api/v1/staff/assignable", async (ArteDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Memberships.AsNoTracking()
                .Where(m => m.IsActive && (m.Role == Roles.Owner || m.Permissions.Contains(Permissions.CasesWork)))
                .OrderBy(m => m.CreatedAt)
                .Select(m => new { m.Id, Name = m.User!.DisplayName ?? m.User.Mobile, m.Role })
                .ToListAsync(ct))).RequirePermission(Permissions.CasesAssign);
    }

    // ───────── queries ─────────

    private static async Task<IResult> ListAsync(string? category, bool? all, bool? mine, string? q, int? page,
        RequestUser me, ArteDbContext db, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var cases = db.Cases.AsNoTracking();
        if (!CaseAccess.CanSeeAll(m) || mine == true) cases = cases.Where(c => c.AssigneeId == m.Id);

        var rows =
            from c in cases
            join s in db.Stages on c.StageId equals s.Id
            join cu in db.Customers.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]) on c.CustomerId equals cu.Id
            join a in db.Assets.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]) on c.AssetId equals a.Id into aj
            from a in aj.DefaultIfEmpty()
            join mem in db.Memberships on c.AssigneeId equals mem.Id into mj
            from mem in mj.DefaultIfEmpty()
            select new { c, s, cu, a, AssigneeName = mem == null ? null : mem.User!.DisplayName ?? mem.User.Mobile };

        if (!string.IsNullOrEmpty(category))
        {
            if (!StageCategories.All.Contains(category)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["category"] = ["دسته نامعتبر است."] });
            rows = rows.Where(r => r.s.Category == category);
        }
        if (all != true && string.IsNullOrEmpty(category)) rows = rows.Where(r => !r.s.IsTerminal);

        var term = q?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            if (term.Length > 60) return Results.ValidationProblem(new Dictionary<string, string[]> { ["q"] = ["عبارت جستجو طولانی است."] });
            if (long.TryParse(term.TrimStart('#'), out var number) && term.Length <= 8)
                rows = rows.Where(r => r.c.Number == number);
            else
            {
                var like = $"%{term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
                rows = rows.Where(r => r.cu.Mobile.Contains(term)
                    || (r.cu.FullName != null && EF.Functions.ILike(r.cu.FullName, like, "\\"))
                    || (r.a != null && (EF.Functions.ILike(r.a.Title, like, "\\") || (r.a.Identifier != null && EF.Functions.ILike(r.a.Identifier, like, "\\")))));
            }
        }

        var p = Math.Clamp(page ?? 1, 1, 1000);
        var items = await rows
            .OrderBy(r => r.s.IsTerminal).ThenByDescending(r => r.c.OpenedAt)
            .Skip((p - 1) * 30).Take(30)
            .Select(r => new
            {
                r.c.Id, r.c.Number,
                CustomerName = r.cu.FullName, CustomerMobile = r.cu.Mobile,
                AssetTitle = r.a == null ? null : r.a.Title,
                Stage = new { r.s.Id, r.s.Key, r.s.Name, r.s.Category, r.s.Color, r.s.IsTerminal },
                r.AssigneeName, r.c.StageEnteredAt, r.c.PromisedAt, r.c.OpenedAt, r.c.Request,
            })
            .ToListAsync(ct);
        return Results.Ok(items);
    }

    private static async Task<IResult> DetailAsync(Guid id, RequestUser me, ArteDbContext db, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null || !CaseAccess.CanSee(me.RequiredMembership, c)) return Results.NotFound();
        return Results.Ok(await BuildDetail(c, me.RequiredMembership, db, ct));
    }

    private static async Task<object> BuildDetail(Case c, Membership me, ArteDbContext db, CancellationToken ct)
    {
        var customer = await db.Customers.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
            .Where(x => x.Id == c.CustomerId).Select(x => new { x.Id, x.FullName, x.Mobile }).SingleAsync(ct);
        var asset = c.AssetId is null ? null : await db.Assets.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
            .Where(x => x.Id == c.AssetId).Select(x => new { x.Id, x.Title, x.Identifier }).SingleOrDefaultAsync(ct);
        var stages = await db.Stages.AsNoTracking().Where(s => s.WorkflowId == c.WorkflowId).ToDictionaryAsync(s => s.Id, ct);
        var stage = stages[c.StageId];
        var transitions = await db.Transitions.AsNoTracking()
            .Where(t => t.WorkflowId == c.WorkflowId && t.FromStageId == c.StageId).ToListAsync(ct);
        var allowed = transitions
            .Where(t => stages.TryGetValue(t.ToStageId, out var to) && to.IsActive && CaseAccess.CanRun(me, c, t))
            .OrderByDescending(t => t.IsPrimary)
            .Select(t => new
            {
                t.Id, t.Label, t.IsPrimary, t.RequiresReason,
                ToStage = new { stages[t.ToStageId].Name, stages[t.ToStageId].Category, stages[t.ToStageId].Color },
            })
            .ToList();

        var assignee = c.AssigneeId is null ? null : await db.Memberships.AsNoTracking()
            .Where(m => m.Id == c.AssigneeId).Select(m => new { m.Id, Name = m.User!.DisplayName ?? m.User.Mobile }).SingleOrDefaultAsync(ct);
        var parent = c.ParentCaseId is null ? null : await db.Cases.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
            .Where(x => x.Id == c.ParentCaseId).Select(x => new { x.Id, x.Number }).SingleOrDefaultAsync(ct);

        var timeline = await (
            from e in db.CaseEvents.AsNoTracking()
            where e.CaseId == c.Id
            join u in db.Users on e.ActorUserId equals u.Id into uj
            from u in uj.DefaultIfEmpty()
            orderby e.Id descending
            select new { e.Id, e.Type, e.OccurredAt, e.Data, Actor = u == null ? null : u.DisplayName ?? u.Mobile })
            .Take(200).ToListAsync(ct);

        return new
        {
            c.Id, c.Number, c.Request, c.Diagnosis, c.OdometerKm, c.EstimatedAmountRials, c.PromisedAt,
            c.CustodyStatus, Intake = c.IntakeChecklist?.RootElement, c.Relation,
            c.OpenedAt, c.StageEnteredAt, c.ClosedAt,
            Stage = new { stage.Id, stage.Key, stage.Name, stage.Category, stage.Color, stage.IsTerminal },
            Customer = customer, Asset = asset, Assignee = assignee, ParentCase = parent,
            Transitions = allowed,
            CanEdit = CaseAccess.CanWorkOn(me, c) || me.Has(Permissions.CasesCreate),
            CanManage = me.Has(Permissions.CasesCreate),
            CanAssign = me.Has(Permissions.CasesAssign),
            Timeline = timeline.Select(e => new { e.Id, e.Type, e.OccurredAt, Data = e.Data?.RootElement, e.Actor }),
        };
    }

    private static async Task<IResult> DashboardAsync(RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var cases = db.Cases.AsNoTracking();
        if (!CaseAccess.CanSeeAll(m)) cases = cases.Where(c => c.AssigneeId == m.Id);
        var now = clock.UtcNow;
        var stuckBefore = now.AddDays(-StuckAfterDays);
        var tehran = TimeSpan.FromHours(3.5); // Iran has no DST since 2022.
        var startOfDay = new DateTimeOffset(now.ToOffset(tehran).Date, tehran).ToUniversalTime();

        var open = from c in cases join s in db.Stages on c.StageId equals s.Id where !s.IsTerminal select new { c, s };
        var byCategory = await open.GroupBy(x => x.s.Category).Select(g => new { Category = g.Key, Count = g.Count() }).ToListAsync(ct);
        var byStage = await open.GroupBy(x => new { x.s.Id, x.s.Name, x.s.Color, x.s.Order })
            .Select(g => new { g.Key.Id, g.Key.Name, g.Key.Color, g.Key.Order, Count = g.Count() })
            .OrderBy(x => x.Order).ToListAsync(ct);
        var stuck = await open.CountAsync(x => x.c.StageEnteredAt < stuckBefore && x.s.Category != StageCategories.Done, ct);
        var unassigned = await open.CountAsync(x => x.c.AssigneeId == null, ct);
        var mine = await open.CountAsync(x => x.c.AssigneeId == m.Id, ct);
        var deliveredToday = await (from c in cases join s in db.Stages on c.StageId equals s.Id
                                    where s.Key == "delivered" && c.ClosedAt >= startOfDay select c).CountAsync(ct);
        var openedToday = await cases.CountAsync(c => c.OpenedAt >= startOfDay, ct);

        return Results.Ok(new
        {
            Open = byCategory.Sum(x => x.Count),
            ByCategory = byCategory.ToDictionary(x => x.Category, x => x.Count),
            ByStage = byStage,
            Stuck = stuck, StuckAfterDays,
            Unassigned = unassigned, Mine = mine,
            OpenedToday = openedToday, DeliveredToday = deliveredToday,
        });
    }

    private static async Task<IResult> SuggestParentAsync(Guid assetId, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var since = clock.UtcNow.AddDays(-30);
        var recent = await (from c in db.Cases.AsNoTracking()
                            join s in db.Stages on c.StageId equals s.Id
                            where c.AssetId == assetId && s.Key == "delivered" && c.ClosedAt >= since
                            orderby c.ClosedAt descending
                            select new { c.Id, c.Number, c.ClosedAt, c.Request }).FirstOrDefaultAsync(ct);
        return Results.Ok(recent);
    }

    private static async Task<IResult> TrashAsync(ArteDbContext db, CancellationToken ct) =>
        Results.Ok(await (from c in db.Cases.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
                          where c.DeletedAt != null
                          join cu in db.Customers.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]) on c.CustomerId equals cu.Id
                          orderby c.DeletedAt descending
                          select new { c.Id, c.Number, CustomerName = cu.FullName, CustomerMobile = cu.Mobile, c.DeletedAt })
            .Take(100).ToListAsync(ct));

    // ───────── commands ─────────

    private static async Task<IResult> CreateAsync(CreateCase req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!Mobile.TryNormalize(req.Mobile, out var mobile)) errors["mobile"] = ["شماره موبایل معتبر نیست."];
        var request = req.Request?.Trim();
        if (string.IsNullOrEmpty(request) || request.Length > 2000) errors["request"] = ["شرح مشکل لازم است (حداکثر ۲۰۰۰ حرف)."];
        if (req.CustomerName is { Length: > 120 }) errors["customerName"] = ["نام حداکثر ۱۲۰ حرف."];
        if (req.OdometerKm is < 0 or > 2_000_000) errors["odometerKm"] = ["کیلومتر نامعتبر است."];
        if (req.EstimatedAmountRials is < 0) errors["estimatedAmountRials"] = ["مبلغ نامعتبر است."];
        if (req.NewAsset is { } na && (string.IsNullOrWhiteSpace(na.Title) || na.Title.Length > 120 || na.Identifier is { Length: > 60 }))
            errors["newAsset"] = ["مدل موتور لازم است (حداکثر ۱۲۰ حرف)."];
        if (req.Relation is not null && !CaseRelations.All.Contains(req.Relation)) errors["relation"] = ["نوع ارتباط نامعتبر است."];
        if (ValidateIntake(req.Intake) is { } intakeError) errors["intake"] = [intakeError];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var now = clock.UtcNow;
        var userId = me.RequiredUserId;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Mobile == mobile, ct);
        if (customer is null)
        {
            customer = new Customer
            {
                Mobile = mobile, FullName = string.IsNullOrWhiteSpace(req.CustomerName) ? null : req.CustomerName.Trim(),
                CreatedAt = now, UpdatedAt = now,
            };
            db.Customers.Add(customer);
        }
        else if (string.IsNullOrEmpty(customer.FullName) && !string.IsNullOrWhiteSpace(req.CustomerName))
        {
            customer.FullName = req.CustomerName.Trim();
        }

        Guid? assetId = null;
        if (req.AssetId is { } aid)
        {
            if (!await db.Assets.AnyAsync(a => a.Id == aid && a.CustomerId == customer.Id, ct))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["assetId"] = ["این موتور متعلق به این مشتری نیست."] });
            assetId = aid;
        }
        else if (req.NewAsset is { } newAsset)
        {
            var asset = new Asset
            {
                CustomerId = customer.Id, Kind = "motorcycle", Title = newAsset.Title!.Trim(),
                Identifier = string.IsNullOrWhiteSpace(newAsset.Identifier) ? null : newAsset.Identifier.Trim(), CreatedAt = now,
            };
            db.Assets.Add(asset);
            assetId = asset.Id;
        }

        if (req.AssigneeId is { } assignee)
        {
            if (!me.RequiredMembership.Has(Permissions.CasesAssign))
                return Results.Problem(statusCode: 403, title: "اجازه تخصیص پرونده را ندارید.");
            if (!await IsAssignable(db, assignee, ct))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["assigneeId"] = ["این همکار قابل تخصیص نیست."] });
        }

        if (req.ParentCaseId is { } parentId && !await db.Cases.AnyAsync(c => c.Id == parentId && c.CustomerId == customer.Id, ct))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["parentCaseId"] = ["پرونده قبلی پیدا نشد."] });

        var workflow = await db.Workflows.AsNoTracking().Include(w => w.Stages).SingleAsync(w => w.IsDefault, ct);
        var firstStage = workflow.Stages.Where(s => s.IsActive && !s.IsTerminal).OrderBy(s => s.Order).First();

        var tenantId = me.RequiredMembership.TenantId;
        var number = (await db.Database
            .SqlQuery<long>($"UPDATE \"Tenants\" SET \"LastCaseNumber\" = \"LastCaseNumber\" + 1 WHERE \"Id\" = {tenantId} RETURNING \"LastCaseNumber\" AS \"Value\"")
            .ToListAsync(ct)).Single();

        var c = new Case
        {
            Number = number, CustomerId = customer.Id, AssetId = assetId, WorkflowId = workflow.Id, StageId = firstStage.Id,
            AssigneeId = req.AssigneeId, Request = request!, OdometerKm = req.OdometerKm,
            EstimatedAmountRials = req.EstimatedAmountRials, PromisedAt = req.PromisedAt?.ToUniversalTime(),
            IntakeChecklist = ToJson(req.Intake),
            ParentCaseId = req.ParentCaseId, Relation = req.ParentCaseId is null ? null : req.Relation ?? CaseRelations.Comeback,
            OpenedAt = now, StageEnteredAt = now, OpenedBy = userId,
        };
        db.Cases.Add(c);
        AddEvent(db, c, CaseEventTypes.Opened, userId, now, new { c.Number, Stage = firstStage.Name, c.Request, c.OdometerKm, c.ParentCaseId });
        if (c.AssigneeId is not null) AddEvent(db, c, CaseEventTypes.Assigned, userId, now, new { To = await MemberName(db, c.AssigneeId.Value, ct) });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Results.Created($"/api/v1/cases/{c.Id}", new { c.Id, c.Number });
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateCase req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();

        var managerFields = req.EstimatedAmountRials is not null || req.PromisedAt is not null || req.CustodyStatus is not null || req.Intake is not null;
        if (managerFields && !m.Has(Permissions.CasesCreate)) return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");
        if (!managerFields && !CaseAccess.CanWorkOn(m, c) && !m.Has(Permissions.CasesCreate))
            return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

        var errors = new Dictionary<string, string[]>();
        if (req.Request is not null && (string.IsNullOrWhiteSpace(req.Request) || req.Request.Length > 2000)) errors["request"] = ["شرح مشکل لازم است (حداکثر ۲۰۰۰ حرف)."];
        if (req.Diagnosis is { Length: > 4000 }) errors["diagnosis"] = ["حداکثر ۴۰۰۰ حرف."];
        if (req.OdometerKm is < 0 or > 2_000_000) errors["odometerKm"] = ["کیلومتر نامعتبر است."];
        if (req.EstimatedAmountRials is < 0) errors["estimatedAmountRials"] = ["مبلغ نامعتبر است."];
        if (req.CustodyStatus is not null and not (CustodyStatuses.InShop or CustodyStatuses.WithCustomer)) errors["custodyStatus"] = ["وضعیت نامعتبر است."];
        if (ValidateIntake(req.Intake) is { } intakeError) errors["intake"] = [intakeError];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var changed = new Dictionary<string, object?>();
        if (req.Request is not null && req.Request.Trim() != c.Request) { c.Request = req.Request.Trim(); changed["request"] = c.Request; }
        if (req.Diagnosis is not null && req.Diagnosis.Trim() != (c.Diagnosis ?? "")) { c.Diagnosis = NullIfEmpty(req.Diagnosis); changed["diagnosis"] = c.Diagnosis; }
        if (req.OdometerKm is not null && req.OdometerKm != c.OdometerKm) { c.OdometerKm = req.OdometerKm; changed["odometerKm"] = c.OdometerKm; }
        if (req.EstimatedAmountRials is not null && req.EstimatedAmountRials != c.EstimatedAmountRials) { c.EstimatedAmountRials = req.EstimatedAmountRials; changed["estimatedAmountRials"] = c.EstimatedAmountRials; }
        if (req.PromisedAt is not null && req.PromisedAt != c.PromisedAt) { c.PromisedAt = req.PromisedAt.Value.ToUniversalTime(); changed["promisedAt"] = c.PromisedAt; }
        if (req.Intake is not null) { c.IntakeChecklist = ToJson(req.Intake); changed["intake"] = req.Intake; }

        var now = clock.UtcNow;
        if (req.CustodyStatus is not null && req.CustodyStatus != c.CustodyStatus)
        {
            c.CustodyStatus = req.CustodyStatus;
            AddEvent(db, c, CaseEventTypes.CustodyChanged, me.RequiredUserId, now, new { c.CustodyStatus });
        }
        if (changed.Count > 0) AddEvent(db, c, CaseEventTypes.Updated, me.RequiredUserId, now, changed);

        return await SaveOr409(db, ct, async () => Results.Ok(await BuildDetail(c, m, db, ct)));
    }

    private static async Task<IResult> TransitionAsync(Guid id, Guid transitionId, RunTransition req, RequestUser me,
        ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();

        var t = await db.Transitions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == transitionId && x.WorkflowId == c.WorkflowId, ct);
        if (t is null) return Results.NotFound();
        if (t.FromStageId != c.StageId)
            return Results.Problem(statusCode: 409, title: "وضعیت پرونده در این فاصله تغییر کرده است. صفحه را تازه کنید.");
        if (!CaseAccess.CanRun(m, c, t)) return Results.Problem(statusCode: 403, title: "اجازه این اقدام را ندارید.");

        var reason = req.Reason?.Trim();
        if (t.RequiresReason && string.IsNullOrEmpty(reason))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["برای این اقدام دلیل لازم است."] });
        if (reason is { Length: > 500 })
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["دلیل حداکثر ۵۰۰ حرف."] });

        var stages = await db.Stages.AsNoTracking().Where(s => s.Id == t.FromStageId || s.Id == t.ToStageId).ToDictionaryAsync(s => s.Id, ct);
        var from = stages[t.FromStageId];
        var to = stages[t.ToStageId];
        if (!to.IsActive) return Results.Problem(statusCode: 409, title: "این مرحله غیرفعال است.");

        var now = clock.UtcNow;
        var userId = me.RequiredUserId;
        c.StageId = to.Id;
        c.StageEnteredAt = now;
        AddEvent(db, c, CaseEventTypes.StageChanged, userId, now,
            new { From = from.Name, To = to.Name, to.Category, Action = t.Label, Reason = reason });

        if (from.IsTerminal && !to.IsTerminal)
        {
            c.ClosedAt = null;
            c.CustodyStatus = CustodyStatuses.InShop;
            AddEvent(db, c, CaseEventTypes.Reopened, userId, now, new { Reason = reason });
        }
        if (to.IsTerminal) c.ClosedAt = now;
        if (to.Key == "delivered")
        {
            c.CustodyStatus = CustodyStatuses.WithCustomer;
            AddEvent(db, c, CaseEventTypes.Delivered, userId, now, new { c.OdometerKm });
        }
        if (to.Category == StageCategories.Cancelled) AddEvent(db, c, CaseEventTypes.Cancelled, userId, now, new { Reason = reason });

        return await SaveOr409(db, ct, async () => Results.Ok(await BuildDetail(c, m, db, ct)));
    }

    private static async Task<IResult> AssignAsync(Guid id, Assign req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Results.NotFound();
        if (req.AssigneeId is { } a && !await IsAssignable(db, a, ct))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["assigneeId"] = ["این همکار قابل تخصیص نیست."] });
        if (c.AssigneeId == req.AssigneeId) return Results.Ok(await BuildDetail(c, me.RequiredMembership, db, ct));

        c.AssigneeId = req.AssigneeId;
        AddEvent(db, c, CaseEventTypes.Assigned, me.RequiredUserId, clock.UtcNow,
            new { To = req.AssigneeId is null ? null : await MemberName(db, req.AssigneeId.Value, ct) });
        return await SaveOr409(db, ct, async () => Results.Ok(await BuildDetail(c, me.RequiredMembership, db, ct)));
    }

    private static async Task<IResult> NoteAsync(Guid id, Note req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null || !CaseAccess.CanSee(me.RequiredMembership, c)) return Results.NotFound();
        var text = req.Text?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 2000)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["متن یادداشت لازم است (حداکثر ۲۰۰۰ حرف)."] });

        AddEvent(db, c, CaseEventTypes.NoteAdded, me.RequiredUserId, clock.UtcNow, new { Text = text });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (c is null) return Results.NotFound();
        var now = clock.UtcNow;
        c.DeletedAt = now;
        c.DeletedBy = me.RequiredUserId;
        AddEvent(db, c, CaseEventTypes.Deleted, me.RequiredUserId, now, new { });
        return await SaveOr409(db, ct, () => Task.FromResult(Results.NoContent()));
    }

    private static async Task<IResult> RestoreAsync(Guid id, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var c = await db.Cases.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter])
            .SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt != null, ct);
        if (c is null) return Results.NotFound();
        if (!await db.Customers.AnyAsync(x => x.Id == c.CustomerId, ct))
            return Results.Problem(statusCode: 409, title: "ابتدا مشتری این پرونده را بازگردانید.");
        c.DeletedAt = null;
        c.DeletedBy = null;
        AddEvent(db, c, CaseEventTypes.Restored, me.RequiredUserId, clock.UtcNow, new { });
        return await SaveOr409(db, ct, async () => Results.Ok(await BuildDetail(c, me.RequiredMembership, db, ct)));
    }

    // ───────── helpers ─────────

    private static void AddEvent(ArteDbContext db, Case c, string type, Guid actor, DateTimeOffset at, object data) =>
        db.CaseEvents.Add(new CaseEvent
        {
            TenantId = c.TenantId, CaseId = c.Id, Type = type, ActorUserId = actor, OccurredAt = at,
            Data = JsonSerializer.SerializeToDocument(data, JsonOptions),
        });

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<IResult> SaveOr409(ArteDbContext db, CancellationToken ct, Func<Task<IResult>> ok)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Results.Problem(statusCode: 409, title: "پرونده هم‌زمان توسط شخص دیگری تغییر کرد. صفحه را تازه کنید.");
        }
        return await ok();
    }

    private static Task<bool> IsAssignable(ArteDbContext db, Guid membershipId, CancellationToken ct) =>
        db.Memberships.AnyAsync(m => m.Id == membershipId && m.IsActive
                                     && (m.Role == Roles.Owner || m.Permissions.Contains(Permissions.CasesWork)), ct);

    private static Task<string?> MemberName(ArteDbContext db, Guid membershipId, CancellationToken ct) =>
        db.Memberships.Where(m => m.Id == membershipId).Select(m => m.User!.DisplayName ?? m.User.Mobile).SingleOrDefaultAsync(ct);

    private static string? ValidateIntake(Dictionary<string, string>? intake) =>
        intake is { } d && (d.Count > 30 || d.Any(kv => kv.Key.Length is 0 or > 40 || kv.Value is null || kv.Value.Length > 200))
            ? "حداکثر ۳۰ مورد؛ عنوان تا ۴۰ و مقدار تا ۲۰۰ حرف."
            : null;

    private static JsonDocument? ToJson(Dictionary<string, string>? d) =>
        d is null or { Count: 0 } ? null : JsonSerializer.SerializeToDocument(d);

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
