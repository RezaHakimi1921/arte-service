using System.Text.Json;
using Arte.Api.Cases;
using Arte.Api.Security;
using Arte.Core.Billing;
using Arte.Core.Cases;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Billing;

public static class BillingEndpoints
{
    public sealed record ItemInput(
        string? Kind, string? Title, decimal? Quantity, long? UnitPriceRials, long? UnitCostRials, long? DiscountRials,
        string? Supplier, string? Status, Guid? PerformedBy, int? WarrantyDays, Guid? CatalogItemId);

    public sealed record PaymentInput(long? AmountRials, string? Method, string? Note, DateTimeOffset? PaidAt);

    public sealed record CatalogInput(string? Kind, string? Title, long? DefaultPriceRials, long? DefaultCostRials, int? DefaultWarrantyDays, bool? IsActive);

    private const long MaxRials = 1_000_000_000_000; // 100 billion toman: anything above is a typo
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapBilling(this IEndpointRouteBuilder app)
    {
        var cases = app.MapGroup("/api/v1/cases/{caseId:guid}").RequireTenant();
        cases.MapPost("/items", AddItemAsync);
        cases.MapPatch("/items/{itemId:guid}", UpdateItemAsync);
        cases.MapDelete("/items/{itemId:guid}", RemoveItemAsync);
        cases.MapPost("/items/{itemId:guid}/restore", RestoreItemAsync);
        cases.MapPost("/payments", AddPaymentAsync).RequirePermission(Permissions.PaymentsRecord);
        cases.MapDelete("/payments/{paymentId:guid}", VoidPaymentAsync).RequirePermission(Permissions.PaymentsRecord);

        var catalog = app.MapGroup("/api/v1/catalog").RequireTenant();
        catalog.MapGet("/", ListCatalogAsync);
        catalog.MapPost("/", CreateCatalogAsync).RequirePermission(Permissions.CasesCreate);
        catalog.MapPatch("/{id:guid}", UpdateCatalogAsync).RequirePermission(Permissions.CasesCreate);

        app.MapGet("/api/v1/receivables", ReceivablesAsync).RequireTenant();
    }

    // ───────── items ─────────

    /// <summary>Anyone who works on the case adds parts and labor; only cost-viewers set or see purchase prices.</summary>
    private static async Task<IResult> AddItemAsync(Guid caseId, ItemInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
        if (!CanEditMoney(m, c) || await IsClosed(db, c, ct) && !m.Has(Permissions.CasesCreate))
            return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

        var errors = Validate(req, isNew: true, m);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (req.PerformedBy is { } pb && !await db.Memberships.AnyAsync(x => x.Id == pb && x.IsActive, ct))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["performedBy"] = ["همکار پیدا نشد."] });

        CatalogItem? cat = null;
        if (req.CatalogItemId is { } cid)
        {
            cat = await db.CatalogItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == cid && x.IsActive, ct);
            if (cat is null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["catalogItemId"] = ["قلم انتخاب‌شده پیدا نشد."] });
        }

        var kind = req.Kind ?? cat?.Kind ?? ItemKinds.Part;
        var now = clock.UtcNow;
        var item = new CaseItem
        {
            CaseId = c.Id,
            Kind = kind,
            Title = (req.Title ?? cat?.Title)!.Trim(),
            Quantity = req.Quantity ?? 1,
            UnitPriceRials = req.UnitPriceRials ?? cat?.DefaultPriceRials ?? 0,
            UnitCostRials = m.Has(Permissions.ReportsView) ? req.UnitCostRials ?? cat?.DefaultCostRials : cat?.DefaultCostRials,
            DiscountRials = req.DiscountRials ?? 0,
            Supplier = kind == ItemKinds.Part ? req.Supplier ?? Suppliers.Shop : Suppliers.Shop,
            Status = req.Status ?? ItemStatuses.Used,
            // Labor defaults to whoever records it, so commission reports work without extra taps.
            PerformedBy = req.PerformedBy ?? (kind == ItemKinds.Labor ? m.Id : null),
            WarrantyDays = req.WarrantyDays ?? cat?.DefaultWarrantyDays,
            CatalogItemId = cat?.Id,
            AddedBy = me.RequiredUserId,
            AddedAt = now,
            UpdatedAt = now,
        };
        if (PriceError(item) is { } priceError) return priceError;
        db.CaseItems.Add(item);
        AddEvent(db, c, CaseEventTypes.ItemAdded, me.RequiredUserId, now,
            new { item.Kind, item.Title, item.Quantity, item.Supplier, item.Status, Total = item.LineTotalRials });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/cases/{c.Id}/items/{item.Id}", await MoneyView(db, c.Id, m, ct));
    }

    private static async Task<IResult> UpdateItemAsync(Guid caseId, Guid itemId, ItemInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
        var item = await db.CaseItems.SingleOrDefaultAsync(x => x.Id == itemId && x.CaseId == caseId, ct);
        if (item is null) return Results.NotFound();
        if (!CanEditMoney(m, c) || await IsClosed(db, c, ct) && !m.Has(Permissions.CasesCreate))
            return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

        var errors = Validate(req, isNew: false, m);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        if (req.Title is not null) item.Title = req.Title.Trim();
        if (req.Quantity is not null) item.Quantity = req.Quantity.Value;
        if (req.UnitPriceRials is not null) item.UnitPriceRials = req.UnitPriceRials.Value;
        if (req.UnitCostRials is not null && m.Has(Permissions.ReportsView)) item.UnitCostRials = req.UnitCostRials;
        if (req.DiscountRials is not null) item.DiscountRials = req.DiscountRials.Value;
        if (req.Supplier is not null && item.Kind == ItemKinds.Part) item.Supplier = req.Supplier;
        if (req.Status is not null) item.Status = req.Status;
        if (req.PerformedBy is not null) item.PerformedBy = req.PerformedBy;
        if (req.WarrantyDays is not null) item.WarrantyDays = req.WarrantyDays == 0 ? null : req.WarrantyDays;
        item.UpdatedAt = clock.UtcNow;
        if (PriceError(item) is { } priceError) return priceError;

        AddEvent(db, c, CaseEventTypes.ItemUpdated, me.RequiredUserId, item.UpdatedAt,
            new { item.Title, item.Quantity, item.Status, item.Supplier, Total = item.LineTotalRials });
        await db.SaveChangesAsync(ct);
        return Results.Ok(await MoneyView(db, c.Id, m, ct));
    }

    private static async Task<IResult> RemoveItemAsync(Guid caseId, Guid itemId, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
        var item = await db.CaseItems.SingleOrDefaultAsync(x => x.Id == itemId && x.CaseId == caseId, ct);
        if (item is null) return Results.NotFound();
        if (!CanEditMoney(m, c) || await IsClosed(db, c, ct) && !m.Has(Permissions.CasesCreate))
            return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

        item.DeletedAt = clock.UtcNow;
        item.DeletedBy = me.RequiredUserId;
        AddEvent(db, c, CaseEventTypes.ItemRemoved, me.RequiredUserId, item.DeletedAt.Value, new { item.Title });
        await db.SaveChangesAsync(ct);
        return Results.Ok(await MoneyView(db, c.Id, m, ct));
    }

    private static async Task<IResult> RestoreItemAsync(Guid caseId, Guid itemId, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
        if (!CanEditMoney(m, c)) return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");
        var item = await db.CaseItems.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter])
            .SingleOrDefaultAsync(x => x.Id == itemId && x.CaseId == caseId && x.DeletedAt != null, ct);
        if (item is null) return Results.NotFound();
        item.DeletedAt = null;
        item.DeletedBy = null;
        AddEvent(db, c, CaseEventTypes.ItemAdded, me.RequiredUserId, clock.UtcNow, new { item.Title, Restored = true });
        await db.SaveChangesAsync(ct);
        return Results.Ok(await MoneyView(db, c.Id, m, ct));
    }

    // ───────── payments ─────────

    private static async Task<IResult> AddPaymentAsync(Guid caseId, PaymentInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null) return Results.NotFound();

        var errors = new Dictionary<string, string[]>();
        if (req.AmountRials is not > 0 || req.AmountRials > MaxRials) errors["amountRials"] = ["مبلغ نامعتبر است."];
        if (req.Method is null || !PaymentMethods.All.Contains(req.Method)) errors["method"] = ["روش پرداخت را انتخاب کنید."];
        if (req.Note is { Length: > 300 }) errors["note"] = ["حداکثر ۳۰۰ حرف."];
        var now = clock.UtcNow;
        if (req.PaidAt is { } at && (at > now.AddMinutes(5) || at < now.AddYears(-1))) errors["paidAt"] = ["تاریخ پرداخت نامعتبر است."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var payment = new Payment
        {
            CaseId = c.Id, CustomerId = c.CustomerId, AmountRials = req.AmountRials!.Value, Method = req.Method!,
            PaidAt = (req.PaidAt ?? now).ToUniversalTime(), RecordedBy = me.RequiredUserId,
            Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
        };
        db.Payments.Add(payment);
        AddEvent(db, c, CaseEventTypes.PaymentRecorded, me.RequiredUserId, now, new { payment.AmountRials, payment.Method, payment.Note });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/cases/{c.Id}/payments/{payment.Id}", await MoneyView(db, c.Id, m, ct));
    }

    /// <summary>A wrong payment is voided (soft), never erased: the timeline keeps both events.</summary>
    private static async Task<IResult> VoidPaymentAsync(Guid caseId, Guid paymentId, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null) return Results.NotFound();
        var p = await db.Payments.SingleOrDefaultAsync(x => x.Id == paymentId && x.CaseId == caseId, ct);
        if (p is null) return Results.NotFound();
        p.DeletedAt = clock.UtcNow;
        p.DeletedBy = me.RequiredUserId;
        AddEvent(db, c, CaseEventTypes.PaymentVoided, me.RequiredUserId, p.DeletedAt.Value, new { p.AmountRials, p.Method });
        await db.SaveChangesAsync(ct);
        return Results.Ok(await MoneyView(db, c.Id, me.RequiredMembership, ct));
    }

    // ───────── catalog ─────────

    private static async Task<IResult> ListCatalogAsync(string? q, string? kind, bool? all, RequestUser me, ArteDbContext db, CancellationToken ct)
    {
        var items = db.CatalogItems.AsNoTracking();
        if (all != true) items = items.Where(x => x.IsActive);
        if (!string.IsNullOrEmpty(kind)) items = items.Where(x => x.Kind == kind);
        var term = q?.Trim();
        if (!string.IsNullOrEmpty(term) && term.Length <= 60)
        {
            var like = $"%{term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_")}%";
            items = items.Where(x => EF.Functions.ILike(x.Title, like, "\\"));
        }
        var showCost = me.RequiredMembership.Has(Permissions.ReportsView);
        return Results.Ok(await items.OrderBy(x => x.Title).Take(50)
            .Select(x => new
            {
                x.Id, x.Kind, x.Title, x.DefaultPriceRials,
                DefaultCostRials = showCost ? x.DefaultCostRials : null,
                x.DefaultWarrantyDays, x.IsActive,
            })
            .ToListAsync(ct));
    }

    private static async Task<IResult> CreateCatalogAsync(CatalogInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var errors = ValidateCatalog(req, isNew: true);
        if (req.DefaultCostRials is { } dc && dc > (req.DefaultPriceRials ?? 0)) errors["defaultCostRials"] = ["قیمت خرید نمی‌تواند از قیمت فروش بیشتر باشد."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var item = new CatalogItem
        {
            Kind = req.Kind!, Title = req.Title!.Trim(), DefaultPriceRials = req.DefaultPriceRials ?? 0,
            DefaultCostRials = me.RequiredMembership.Has(Permissions.ReportsView) ? req.DefaultCostRials : null,
            DefaultWarrantyDays = req.DefaultWarrantyDays, CreatedAt = clock.UtcNow,
        };
        db.CatalogItems.Add(item);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/catalog/{item.Id}", new { item.Id });
    }

    private static async Task<IResult> UpdateCatalogAsync(Guid id, CatalogInput req, RequestUser me, ArteDbContext db, CancellationToken ct)
    {
        var errors = ValidateCatalog(req, isNew: false);
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        var item = await db.CatalogItems.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Results.NotFound();
        if (req.Title is not null) item.Title = req.Title.Trim();
        if (req.Kind is not null) item.Kind = req.Kind;
        if (req.DefaultPriceRials is not null) item.DefaultPriceRials = req.DefaultPriceRials.Value;
        if (req.DefaultCostRials is not null && me.RequiredMembership.Has(Permissions.ReportsView)) item.DefaultCostRials = req.DefaultCostRials;
        if (req.DefaultWarrantyDays is not null) item.DefaultWarrantyDays = req.DefaultWarrantyDays == 0 ? null : req.DefaultWarrantyDays;
        if (req.IsActive is not null) item.IsActive = req.IsActive.Value;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ───────── receivables (نسیه) ─────────

    private static async Task<IResult> ReceivablesAsync(RequestUser me, ArteDbContext db, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        if (!m.Has(Permissions.PaymentsRecord) && !m.Has(Permissions.ReportsView))
            return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

        var delivered = await (from c in db.Cases.AsNoTracking()
                               join s in db.Stages on c.StageId equals s.Id
                               where s.Key == "delivered"
                               join cu in db.Customers.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]) on c.CustomerId equals cu.Id
                               select new { c.Id, c.Number, c.ClosedAt, c.CreditDueAt, cu.FullName, cu.Mobile, CustomerId = cu.Id })
            .ToListAsync(ct);
        var balances = await Balances(db, delivered.Select(d => d.Id).ToList(), ct);
        var rows = delivered
            .Select(d => new { d.Id, d.Number, d.ClosedAt, d.CreditDueAt, d.FullName, d.Mobile, d.CustomerId, BalanceRials = balances.GetValueOrDefault(d.Id) })
            .Where(r => r.BalanceRials > 0)
            .OrderBy(r => r.CreditDueAt ?? r.ClosedAt)
            .ToList();
        return Results.Ok(new { TotalRials = rows.Sum(r => r.BalanceRials), Cases = rows });
    }

    // ───────── shared ─────────

    /// <summary>Items, payments and totals for the case page. Purchase prices and profit only for cost-viewers.</summary>
    public static async Task<object> MoneyView(ArteDbContext db, Guid caseId, Membership m, CancellationToken ct)
    {
        var items = await db.CaseItems.AsNoTracking().Where(x => x.CaseId == caseId).OrderBy(x => x.AddedAt).ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking().Where(x => x.CaseId == caseId).OrderBy(x => x.PaidAt).ToListAsync(ct);
        var performerIds = items.Where(i => i.PerformedBy != null).Select(i => i.PerformedBy!.Value).Distinct().ToList();
        var names = await db.Memberships.AsNoTracking().Where(x => performerIds.Contains(x.Id))
            .Select(x => new { x.Id, Name = x.User!.DisplayName ?? x.User.Mobile }).ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var money = CaseMoney.Of(items, payments);
        var showCost = m.Has(Permissions.ReportsView);

        return new
        {
            Items = items.Select(i => new
            {
                i.Id, i.Kind, i.Title, i.Quantity, i.UnitPriceRials,
                UnitCostRials = showCost ? i.UnitCostRials : null,
                i.DiscountRials, i.Supplier, i.Status, i.PerformedBy,
                PerformedByName = i.PerformedBy is { } pb ? names.GetValueOrDefault(pb) : null,
                i.WarrantyDays, i.LineTotalRials,
                ProfitRials = showCost && i.UnitCostRials != null ? i.LineTotalRials - i.LineCostRials : (long?)null,
            }),
            Payments = payments.Select(p => new { p.Id, p.AmountRials, p.Method, p.PaidAt, p.Note }),
            Money = new
            {
                money.TotalRials, money.PaidRials, money.BalanceRials, money.PartsRials, money.LaborRials, money.ServicesRials,
                CostRials = showCost ? money.CostRials : (long?)null,
                ProfitRials = showCost ? money.ProfitRials : (long?)null,
            },
            CanEditItems = true,
            CanRecordPayments = m.Has(Permissions.PaymentsRecord),
            CanSeeCost = showCost,
        };
    }

    public static async Task<Dictionary<Guid, long>> Balances(ArteDbContext db, List<Guid> caseIds, CancellationToken ct)
    {
        if (caseIds.Count == 0) return [];
        var items = await db.CaseItems.AsNoTracking().Where(x => caseIds.Contains(x.CaseId)).ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking().Where(x => caseIds.Contains(x.CaseId)).ToListAsync(ct);
        var itemsBy = items.ToLookup(x => x.CaseId);
        var paysBy = payments.ToLookup(x => x.CaseId);
        return caseIds.ToDictionary(id => id, id => CaseMoney.Of(itemsBy[id], paysBy[id]).BalanceRials);
    }

    /// <summary>A shop part bought for more than it is sold is almost always a typo (spec 08 R4).</summary>
    private static IResult? PriceError(CaseItem item) =>
        item.Supplier == Suppliers.Shop && item.UnitCostRials is { } cost && cost > item.UnitPriceRials
            ? Results.ValidationProblem(new Dictionary<string, string[]> { ["unitCostRials"] = ["قیمت خرید نمی‌تواند از قیمت فروش بیشتر باشد."] })
            : null;

    private static bool CanEditMoney(Membership m, Case c) => CaseAccess.CanWorkOn(m, c) || m.Has(Permissions.CasesCreate);

    private static Task<bool> IsClosed(ArteDbContext db, Case c, CancellationToken ct) =>
        db.Stages.AnyAsync(s => s.Id == c.StageId && s.IsTerminal, ct);

    private static Dictionary<string, string[]> Validate(ItemInput req, bool isNew, Membership m)
    {
        var e = new Dictionary<string, string[]>();
        if (req.Kind is not null && !ItemKinds.All.Contains(req.Kind)) e["kind"] = ["نوع ردیف نامعتبر است."];
        if (isNew && string.IsNullOrWhiteSpace(req.Title) && req.CatalogItemId is null) e["title"] = ["عنوان لازم است."];
        if (req.Title is { Length: > 120 } || req.Title is { } t && string.IsNullOrWhiteSpace(t) && !isNew) e["title"] = ["عنوان ۱ تا ۱۲۰ حرف."];
        if (req.Quantity is { } q && (q <= 0 || q > 10_000)) e["quantity"] = ["تعداد نامعتبر است."];
        if (req.UnitPriceRials is < 0 or > MaxRials) e["unitPriceRials"] = ["قیمت فروش نامعتبر است."];
        if (req.UnitCostRials is < 0 or > MaxRials) e["unitCostRials"] = ["قیمت خرید نامعتبر است."];
        if (req.DiscountRials is < 0 or > MaxRials) e["discountRials"] = ["تخفیف نامعتبر است."];
        if (req.Supplier is not null and not (Suppliers.Shop or Suppliers.Customer)) e["supplier"] = ["تأمین‌کننده نامعتبر است."];
        if (req.Status is not null and not (ItemStatuses.Needed or ItemStatuses.Used)) e["status"] = ["وضعیت نامعتبر است."];
        if (req.WarrantyDays is < 0 or > 3650) e["warrantyDays"] = ["ضمانت نامعتبر است."];
        if (req.UnitCostRials is not null && !m.Has(Permissions.ReportsView)) e["unitCostRials"] = ["ثبت قیمت خرید فقط برای مالک است."];
        return e;
    }

    private static Dictionary<string, string[]> ValidateCatalog(CatalogInput req, bool isNew)
    {
        var e = new Dictionary<string, string[]>();
        if (isNew && (req.Kind is null || !ItemKinds.All.Contains(req.Kind))) e["kind"] = ["نوع لازم است."];
        if (!isNew && req.Kind is not null && !ItemKinds.All.Contains(req.Kind)) e["kind"] = ["نوع نامعتبر است."];
        if (isNew && string.IsNullOrWhiteSpace(req.Title)) e["title"] = ["عنوان لازم است."];
        if (req.Title is { Length: > 120 }) e["title"] = ["حداکثر ۱۲۰ حرف."];
        if (req.DefaultPriceRials is < 0 or > MaxRials) e["defaultPriceRials"] = ["قیمت نامعتبر است."];
        if (req.DefaultCostRials is < 0 or > MaxRials) e["defaultCostRials"] = ["قیمت خرید نامعتبر است."];
        if (req.DefaultWarrantyDays is < 0 or > 3650) e["defaultWarrantyDays"] = ["ضمانت نامعتبر است."];
        return e;
    }

    private static void AddEvent(ArteDbContext db, Case c, string type, Guid actor, DateTimeOffset at, object data) =>
        db.CaseEvents.Add(new CaseEvent
        {
            TenantId = c.TenantId, CaseId = c.Id, Type = type, ActorUserId = actor, OccurredAt = at,
            Data = JsonSerializer.SerializeToDocument(data, Json),
        });
}
