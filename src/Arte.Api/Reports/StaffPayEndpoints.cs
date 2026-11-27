using System.Text.Json;
using Arte.Api.Security;
using Arte.Core.Billing;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Reports;

public static class StaffPayEndpoints
{
    private const long MaxRials = 100_000_000_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record BonusInput(Guid MembershipId, long AmountRials, string? Title, int JalaliYear, int JalaliMonth);
    public sealed record BonusPatch(long? AmountRials, string? Title, int? JalaliYear, int? JalaliMonth, string? Reason);
    public sealed record DeductionInput(Guid MembershipId, long AmountRials, string? Category, int JalaliYear, int JalaliMonth, Guid? ShopLossId);
    public sealed record DeductionPatch(long? AmountRials, string? Category, int? JalaliYear, int? JalaliMonth, Guid? ShopLossId, string? Reason);
    public sealed record VoidInput(string? Reason);
    public sealed record SettlementInput(Guid MembershipId, int JalaliYear, int JalaliMonth);

    public static void MapStaffPay(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/reports/staff-pay", GetAsync).RequirePermission(Permissions.ReportsView);

        var g = app.MapGroup("/api/v1/reports/staff-pay").RequirePermission(Permissions.StaffManage);
        g.MapPost("/bonuses", CreateBonusAsync);
        g.MapPatch("/bonuses/{id:guid}", PatchBonusAsync);
        g.MapPost("/bonuses/{id:guid}/void", VoidBonusAsync);
        g.MapPost("/deductions", CreateDeductionAsync);
        g.MapPatch("/deductions/{id:guid}", PatchDeductionAsync);
        g.MapPost("/deductions/{id:guid}/void", VoidDeductionAsync);
        g.MapPost("/settlements", ConfirmSettlementAsync);
    }

    private static async Task<IResult> GetAsync(DateTimeOffset from, DateTimeOffset to, ArteDbContext db, CancellationToken ct)
    {
        from = from.ToUniversalTime();
        to = to.ToUniversalTime();
        if (to <= from || to - from > TimeSpan.FromDays(400))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["بازه نامعتبر است."] });
        var staff = await StaffPayCalculator.BuildAsync(db, from, to, ct);
        return Results.Ok(new { From = from, To = to, Staff = staff });
    }

    private static async Task<IResult> CreateBonusAsync(BonusInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var title = req.Title?.Trim();
        if (string.IsNullOrEmpty(title) || title.Length > 200) errors["title"] = ["عنوان پاداش لازم است (حداکثر ۲۰۰ حرف)."];
        if (req.AmountRials is <= 0 or > MaxRials) errors["amountRials"] = ["مبلغ نامعتبر است."];
        if (!Jalali.ValidMonth(req.JalaliYear, req.JalaliMonth)) errors["jalaliMonth"] = ["ماه شمسی نامعتبر است."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (await db.Memberships.AnyAsync(m => m.Id == req.MembershipId, ct) is false) return Results.NotFound();

        var now = clock.UtcNow;
        var row = new StaffBonus
        {
            MembershipId = req.MembershipId, AmountRials = req.AmountRials, Title = title!,
            JalaliYear = req.JalaliYear, JalaliMonth = req.JalaliMonth, CreatedBy = me.RequiredUserId, CreatedAt = now,
        };
        db.StaffBonuses.Add(row);
        Audit(db, StaffPayEntityTypes.Bonus, row.Id, StaffPayActions.Create, null, BonusSnap(row), null, me.RequiredUserId, now);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/reports/staff-pay/bonuses/{row.Id}", new { row.Id, row.AmountRials, row.Title, row.JalaliYear, row.JalaliMonth });
    }

    private static async Task<IResult> PatchBonusAsync(Guid id, BonusPatch req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var row = await db.StaffBonuses.SingleOrDefaultAsync(b => b.Id == id, ct);
        if (row is null) return Results.NotFound();
        if (row.VoidedAt is not null) return Results.Problem(statusCode: 409, title: "پاداش باطل‌شده قابل ویرایش نیست.");

        var errors = new Dictionary<string, string[]>();
        var title = req.Title?.Trim();
        if (req.Title is not null && (string.IsNullOrEmpty(title) || title.Length > 200)) errors["title"] = ["عنوان پاداش لازم است (حداکثر ۲۰۰ حرف)."];
        if (req.AmountRials is <= 0 or > MaxRials) errors["amountRials"] = ["مبلغ نامعتبر است."];
        var year = req.JalaliYear ?? row.JalaliYear;
        var month = req.JalaliMonth ?? row.JalaliMonth;
        if (!Jalali.ValidMonth(year, month)) errors["jalaliMonth"] = ["ماه شمسی نامعتبر است."];
        var reason = CleanReason(req.Reason, errors);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var before = BonusSnap(row);
        if (title is not null) row.Title = title;
        if (req.AmountRials is { } amount) row.AmountRials = amount;
        row.JalaliYear = year;
        row.JalaliMonth = month;
        var now = clock.UtcNow;
        Audit(db, StaffPayEntityTypes.Bonus, row.Id, StaffPayActions.Edit, before, BonusSnap(row), reason, me.RequiredUserId, now);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> VoidBonusAsync(Guid id, VoidInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var row = await db.StaffBonuses.SingleOrDefaultAsync(b => b.Id == id, ct);
        if (row is null) return Results.NotFound();
        if (row.VoidedAt is not null) return Results.Problem(statusCode: 409, title: "این پاداش قبلاً باطل شده است.");
        var errors = new Dictionary<string, string[]>();
        var reason = CleanReason(req.Reason, errors, required: true);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var before = BonusSnap(row);
        var now = clock.UtcNow;
        row.VoidedAt = now;
        row.VoidedBy = me.RequiredUserId;
        row.VoidReason = reason;
        Audit(db, StaffPayEntityTypes.Bonus, row.Id, StaffPayActions.Void, before, BonusSnap(row), reason, me.RequiredUserId, now);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> CreateDeductionAsync(DeductionInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        var category = req.Category?.Trim();
        if (category is null || !DeductionCategories.All.Contains(category)) errors["category"] = ["نوع کسر نامعتبر است."];
        if (req.AmountRials is <= 0 or > MaxRials) errors["amountRials"] = ["مبلغ نامعتبر است."];
        if (!Jalali.ValidMonth(req.JalaliYear, req.JalaliMonth)) errors["jalaliMonth"] = ["ماه شمسی نامعتبر است."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var member = await db.Memberships.SingleOrDefaultAsync(m => m.Id == req.MembershipId, ct);
        if (member is null) return Results.NotFound();
        var lossError = await CheckShopLoss(db, member.Id, category!, req.ShopLossId, ct);
        if (lossError is not null) return lossError;
        var cap = await OverCap(db, member, req.JalaliYear, req.JalaliMonth, req.AmountRials, exceptId: null, ct);
        if (cap is not null) return cap;

        var now = clock.UtcNow;
        var row = new StaffDeduction
        {
            MembershipId = member.Id, Category = category!, AmountRials = req.AmountRials,
            JalaliYear = req.JalaliYear, JalaliMonth = req.JalaliMonth, ShopLossId = req.ShopLossId,
            CreatedBy = me.RequiredUserId, CreatedAt = now,
        };
        db.StaffDeductions.Add(row);
        Audit(db, StaffPayEntityTypes.Deduction, row.Id, StaffPayActions.Create, null, DeductionSnap(row), null, me.RequiredUserId, now);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/reports/staff-pay/deductions/{row.Id}", new { row.Id, row.AmountRials, row.Category, row.JalaliYear, row.JalaliMonth });
    }

    private static async Task<IResult> PatchDeductionAsync(Guid id, DeductionPatch req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var row = await db.StaffDeductions.SingleOrDefaultAsync(d => d.Id == id, ct);
        if (row is null) return Results.NotFound();
        if (row.VoidedAt is not null) return Results.Problem(statusCode: 409, title: "کسر باطل‌شده قابل ویرایش نیست.");

        var errors = new Dictionary<string, string[]>();
        var category = req.Category?.Trim() ?? row.Category;
        if (!DeductionCategories.All.Contains(category)) errors["category"] = ["نوع کسر نامعتبر است."];
        var amount = req.AmountRials ?? row.AmountRials;
        if (amount is <= 0 or > MaxRials) errors["amountRials"] = ["مبلغ نامعتبر است."];
        var year = req.JalaliYear ?? row.JalaliYear;
        var month = req.JalaliMonth ?? row.JalaliMonth;
        if (!Jalali.ValidMonth(year, month)) errors["jalaliMonth"] = ["ماه شمسی نامعتبر است."];
        var reason = CleanReason(req.Reason, errors);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var shopLossId = req.ShopLossId ?? (category == DeductionCategories.LossLinked ? row.ShopLossId : null);
        if (category != DeductionCategories.LossLinked) shopLossId = null;
        var lossError = await CheckShopLoss(db, row.MembershipId, category, shopLossId, ct);
        if (lossError is not null) return lossError;

        var member = await db.Memberships.SingleAsync(m => m.Id == row.MembershipId, ct);
        var cap = await OverCap(db, member, year, month, amount, row.Id, ct);
        if (cap is not null) return cap;

        var before = DeductionSnap(row);
        row.Category = category;
        row.AmountRials = amount;
        row.JalaliYear = year;
        row.JalaliMonth = month;
        row.ShopLossId = shopLossId;
        var now = clock.UtcNow;
        Audit(db, StaffPayEntityTypes.Deduction, row.Id, StaffPayActions.Edit, before, DeductionSnap(row), reason, me.RequiredUserId, now);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> VoidDeductionAsync(Guid id, VoidInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var row = await db.StaffDeductions.SingleOrDefaultAsync(d => d.Id == id, ct);
        if (row is null) return Results.NotFound();
        if (row.VoidedAt is not null) return Results.Problem(statusCode: 409, title: "این کسر قبلاً باطل شده است.");
        var errors = new Dictionary<string, string[]>();
        var reason = CleanReason(req.Reason, errors, required: true);
        if (errors.Count > 0) return Results.ValidationProblem(errors);

        var before = DeductionSnap(row);
        var now = clock.UtcNow;
        row.VoidedAt = now;
        row.VoidedBy = me.RequiredUserId;
        row.VoidReason = reason;
        Audit(db, StaffPayEntityTypes.Deduction, row.Id, StaffPayActions.Void, before, DeductionSnap(row), reason, me.RequiredUserId, now);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> ConfirmSettlementAsync(SettlementInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        if (!Jalali.ValidMonth(req.JalaliYear, req.JalaliMonth))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["jalaliMonth"] = ["ماه شمسی نامعتبر است."] });
        var member = await db.Memberships.SingleOrDefaultAsync(m => m.Id == req.MembershipId, ct);
        if (member is null) return Results.NotFound();
        if (await db.StaffPaySettlements.AnyAsync(s => s.MembershipId == member.Id && s.JalaliYear == req.JalaliYear && s.JalaliMonth == req.JalaliMonth, ct))
            return Results.Problem(statusCode: 409, title: "این ماه قبلاً تأیید شده است.");

        var start = Jalali.MonthStartUtc(req.JalaliYear, req.JalaliMonth);
        var end = Jalali.MonthEndUtc(req.JalaliYear, req.JalaliMonth);
        var fixedSalary = Jalali.ProrateFixedMonthly(member.FixedMonthlyRials ?? 0, member.EmploymentStartedOn, member.EmploymentEndedOn, start, end);
        var commission = await db.CaseCommissionGrants.AsNoTracking()
            .Where(g => g.MembershipId == member.Id && g.GrantedAt >= start && g.GrantedAt < end)
            .SumAsync(g => (long?)g.CommissionRials, ct) ?? 0;
        var bonus = await db.StaffBonuses.AsNoTracking()
            .Where(b => b.MembershipId == member.Id && b.JalaliYear == req.JalaliYear && b.JalaliMonth == req.JalaliMonth && b.VoidedAt == null)
            .SumAsync(b => (long?)b.AmountRials, ct) ?? 0;
        var deduction = await StaffPayCalculator.ActiveDeductionsAsync(db, member.Id, req.JalaliYear, req.JalaliMonth, null, ct);
        var shopLoss = await StaffPayCalculator.ShopLossRialsAsync(db, member.Id, start, end, ct);
        var income = fixedSalary + commission + bonus;
        var now = clock.UtcNow;
        var row = new StaffPaySettlement
        {
            MembershipId = member.Id, JalaliYear = req.JalaliYear, JalaliMonth = req.JalaliMonth,
            FixedSalaryRials = fixedSalary, CommissionRials = commission, BonusRials = bonus, DeductionRials = deduction,
            ShopLossRials = shopLoss, IncomeRials = income, AfterDeductionsRials = income - deduction,
            ConfirmedAt = now, ConfirmedBy = me.RequiredUserId,
        };
        db.StaffPaySettlements.Add(row);
        Audit(db, StaffPayEntityTypes.Settlement, row.Id, StaffPayActions.Create, null, SettlementSnap(row), null, me.RequiredUserId, now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Results.Problem(statusCode: 409, title: "این ماه قبلاً تأیید شده است.");
        }
        return Results.Created($"/api/v1/reports/staff-pay/settlements/{row.Id}", SettlementSnap(row));
    }

    private static async Task<IResult?> OverCap(ArteDbContext db, Membership member, int year, int month, long amount, Guid? exceptId, CancellationToken ct)
    {
        var income = await StaffPayCalculator.MonthIncomeAsync(db, member, year, month, ct);
        var already = await StaffPayCalculator.ActiveDeductionsAsync(db, member.Id, year, month, exceptId, ct);
        if (already + amount <= income) return null;
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["amountRials"] = ["جمع کسور این ماه از درآمد همان ماه بیشتر است."],
        });
    }

    private static async Task<IResult?> CheckShopLoss(ArteDbContext db, Guid membershipId, string category, Guid? shopLossId, CancellationToken ct)
    {
        if (shopLossId is null) return null;
        if (category != DeductionCategories.LossLinked)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["shopLossId"] = ["اتصال زیان فقط برای کسر مرتبط با زیان است."] });
        var loss = await db.ShopLossReports.AsNoTracking().SingleOrDefaultAsync(l => l.Id == shopLossId, ct);
        if (loss is null)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["shopLossId"] = ["زیان پیدا نشد."] });
        if (loss.ResponsibleUserId != membershipId)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["shopLossId"] = ["این زیان به این کارکن منتسب نیست."] });
        return null;
    }

    private static string? CleanReason(string? reason, Dictionary<string, string[]> errors, bool required = false)
    {
        var text = reason?.Trim();
        if (required && string.IsNullOrEmpty(text)) errors["reason"] = ["علت لازم است."];
        if (text is { Length: > 500 }) errors["reason"] = ["علت حداکثر ۵۰۰ حرف."];
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static void Audit(ArteDbContext db, string entityType, Guid entityId, string action, object? before, object? after, string? reason, Guid userId, DateTimeOffset at) =>
        db.StaffPayAuditLogs.Add(new StaffPayAuditLog
        {
            EntityType = entityType, EntityId = entityId, Action = action,
            BeforeJson = before is null ? null : JsonSerializer.Serialize(before, Json),
            AfterJson = after is null ? null : JsonSerializer.Serialize(after, Json),
            Reason = reason, UserId = userId, At = at,
        });

    private static object BonusSnap(StaffBonus b) => new
    {
        b.Id, b.MembershipId, b.AmountRials, b.Title, b.JalaliYear, b.JalaliMonth, b.VoidedAt, b.VoidReason,
    };

    private static object DeductionSnap(StaffDeduction d) => new
    {
        d.Id, d.MembershipId, d.Category, d.AmountRials, d.JalaliYear, d.JalaliMonth, d.ShopLossId, d.VoidedAt, d.VoidReason,
    };

    private static object SettlementSnap(StaffPaySettlement s) => new
    {
        s.Id, s.MembershipId, s.JalaliYear, s.JalaliMonth, s.FixedSalaryRials, s.CommissionRials, s.BonusRials,
        s.DeductionRials, s.ShopLossRials, s.IncomeRials, s.AfterDeductionsRials, s.ConfirmedAt, s.ConfirmedBy,
    };
}
