using Arte.Core.Billing;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Reports;

public sealed record StaffPayBonusRow(Guid Id, string Title, long AmountRials, int JalaliYear, int JalaliMonth, DateTimeOffset? VoidedAt, string? VoidReason);

public sealed record StaffPayDeductionRow(
    Guid Id, string Category, long AmountRials, string Reason, int JalaliYear, int JalaliMonth, Guid? ShopLossId, DateTimeOffset? VoidedAt, string? VoidReason);

public sealed record StaffPayCommissionCaseRow(
    Guid CaseId, long CaseNumber, DateTimeOffset GrantedAt, long BaseRials, long CommissionRials,
    string CommissionType, decimal? CommissionPercent, long? CommissionFixedRials, string CommissionBase);

public sealed record StaffPaySettlementRow(
    Guid Id, int JalaliYear, int JalaliMonth, long FixedSalaryRials, long CommissionRials, long BonusRials,
    long DeductionRials, long ShopLossRials, long IncomeRials, long AfterDeductionsRials, DateTimeOffset ConfirmedAt, Guid ConfirmedBy);

public sealed record StaffPayRow(
    Guid MembershipId, string Name, string Role, DateOnly EmploymentStartedOn, DateOnly? EmploymentEndedOn,
    int Cases, long BaseRials, long CommissionRials, string CommissionType, decimal? CommissionPercent,
    long? CommissionFixedRials, string CommissionBase, long FixedSalaryRials, long BonusRials, long DeductionRials,
    long IncomeRials, long AfterDeductionsRials, int ShopLossCount, long ShopLossRials,
    IReadOnlyList<StaffPayBonusRow> Bonuses, IReadOnlyList<StaffPayDeductionRow> Deductions,
    IReadOnlyList<StaffPayCommissionCaseRow> CommissionCases, IReadOnlyList<StaffPaySettlementRow> SettlementsInRange);

public readonly record struct ShopLossSlice(Guid Id, Guid ResponsibleMembershipId, long AmountRials, DateTimeOffset Period);

/// <summary>Builds staff-pay rows from grants, prorated salary, full-month bonuses, deductions, and shop losses.</summary>
public static class StaffPayCalculator
{
    public static async Task<IReadOnlyList<StaffPayRow>> BuildAsync(ArteDbContext db, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        from = from.ToUniversalTime();
        to = to.ToUniversalTime();
        var members = await db.Memberships.AsNoTracking().Include(m => m.User).OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var months = Jalali.MonthsOverlapping(from, to);
        var monthSet = months.ToHashSet();
        var years = months.Select(m => m.Year).Distinct().ToArray();

        var grantEntities = await db.CaseCommissionGrants.AsNoTracking()
            .Where(g => g.GrantedAt >= from && g.GrantedAt < to).ToListAsync(ct);
        var caseIds = grantEntities.Select(g => g.CaseId).Distinct().ToList();
        var caseNumbers = caseIds.Count == 0
            ? new Dictionary<Guid, long>()
            : await db.Cases.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
                .Where(c => caseIds.Contains(c.Id))
                .Select(c => new { c.Id, c.Number })
                .ToDictionaryAsync(c => c.Id, c => c.Number, ct);

        var bonuses = years.Length == 0
            ? []
            : (await db.StaffBonuses.AsNoTracking().Where(b => years.Contains(b.JalaliYear)).ToListAsync(ct))
                .Where(b => monthSet.Contains((b.JalaliYear, b.JalaliMonth))).ToList();
        var deductions = years.Length == 0
            ? []
            : (await db.StaffDeductions.AsNoTracking().Where(d => years.Contains(d.JalaliYear)).ToListAsync(ct))
                .Where(d => monthSet.Contains((d.JalaliYear, d.JalaliMonth))).ToList();
        var settlements = years.Length == 0
            ? []
            : (await db.StaffPaySettlements.AsNoTracking().Where(s => years.Contains(s.JalaliYear)).ToListAsync(ct))
                .Where(s => monthSet.Contains((s.JalaliYear, s.JalaliMonth))).ToList();
        var losses = await LossesInRangeAsync(db, from, to, ct);

        var rows = new List<StaffPayRow>();
        foreach (var m in members)
        {
            var mineGrants = grantEntities.Where(g => g.MembershipId == m.Id).ToList();
            var mineBonuses = bonuses.Where(b => b.MembershipId == m.Id).OrderBy(b => b.CreatedAt).ToList();
            var mineDeductions = deductions.Where(d => d.MembershipId == m.Id).OrderBy(d => d.CreatedAt).ToList();
            var mineLosses = losses.Where(l => l.ResponsibleMembershipId == m.Id).ToList();
            var mineSettlements = settlements.Where(s => s.MembershipId == m.Id).OrderBy(s => s.JalaliYear).ThenBy(s => s.JalaliMonth).ToList();
            var overlaps = Jalali.EmploymentOverlaps(m.EmploymentStartedOn, m.EmploymentEndedOn, from, to);
            if (!overlaps && mineGrants.Count == 0 && mineBonuses.Count == 0 && mineDeductions.Count == 0
                && mineLosses.Count == 0 && mineSettlements.Count == 0)
                continue;

            var commission = mineGrants.Sum(g => g.CommissionRials);
            var bonus = mineBonuses.Where(b => b.VoidedAt == null).Sum(b => b.AmountRials);
            var deduction = mineDeductions.Where(d => d.VoidedAt == null).Sum(d => d.AmountRials);
            var fixedSalary = Jalali.ProrateFixedMonthly(m.FixedMonthlyRials ?? 0, m.EmploymentStartedOn, m.EmploymentEndedOn, from, to);
            var income = fixedSalary + commission + bonus;
            rows.Add(new StaffPayRow(
                m.Id, m.DisplayName ?? m.User?.Mobile ?? "", m.Role, m.EmploymentStartedOn, m.EmploymentEndedOn,
                mineGrants.Count, mineGrants.Sum(g => g.BaseRials), commission,
                m.CommissionType, m.CommissionPercent, m.CommissionFixedRials, m.CommissionBase,
                fixedSalary, bonus, deduction, income, income - deduction,
                mineLosses.Count, mineLosses.Sum(l => l.AmountRials),
                mineBonuses.Select(b => new StaffPayBonusRow(b.Id, b.Title, b.AmountRials, b.JalaliYear, b.JalaliMonth, b.VoidedAt, b.VoidReason)).ToList(),
                mineDeductions.Select(d => new StaffPayDeductionRow(d.Id, d.Category, d.AmountRials, d.Reason, d.JalaliYear, d.JalaliMonth, d.ShopLossId, d.VoidedAt, d.VoidReason)).ToList(),
                mineGrants.OrderBy(g => g.GrantedAt).Select(g => new StaffPayCommissionCaseRow(
                    g.CaseId, caseNumbers.GetValueOrDefault(g.CaseId), g.GrantedAt, g.BaseRials, g.CommissionRials,
                    g.CommissionType, g.CommissionPercent, g.CommissionFixedRials, g.CommissionBase)).ToList(),
                mineSettlements.Select(s => new StaffPaySettlementRow(
                    s.Id, s.JalaliYear, s.JalaliMonth, s.FixedSalaryRials, s.CommissionRials, s.BonusRials,
                    s.DeductionRials, s.ShopLossRials, s.IncomeRials, s.AfterDeductionsRials, s.ConfirmedAt, s.ConfirmedBy)).ToList()));
        }
        return rows;
    }

    /// <summary>Prorated fixed salary for the whole Jalali month, plus grants and active bonuses inside that month. Shop loss is not income.</summary>
    public static async Task<long> MonthIncomeAsync(ArteDbContext db, Membership member, int year, int month, CancellationToken ct)
    {
        var start = Jalali.MonthStartUtc(year, month);
        var end = Jalali.MonthEndUtc(year, month);
        var fixedPart = Jalali.ProrateFixedMonthly(member.FixedMonthlyRials ?? 0, member.EmploymentStartedOn, member.EmploymentEndedOn, start, end);
        var commission = await db.CaseCommissionGrants.AsNoTracking()
            .Where(g => g.MembershipId == member.Id && g.GrantedAt >= start && g.GrantedAt < end)
            .SumAsync(g => (long?)g.CommissionRials, ct) ?? 0;
        var bonus = await db.StaffBonuses.AsNoTracking()
            .Where(b => b.MembershipId == member.Id && b.JalaliYear == year && b.JalaliMonth == month && b.VoidedAt == null)
            .SumAsync(b => (long?)b.AmountRials, ct) ?? 0;
        return fixedPart + commission + bonus;
    }

    public static async Task<long> ActiveDeductionsAsync(ArteDbContext db, Guid membershipId, int year, int month, Guid? exceptId, CancellationToken ct) =>
        await db.StaffDeductions.AsNoTracking()
            .Where(d => d.MembershipId == membershipId && d.JalaliYear == year && d.JalaliMonth == month && d.VoidedAt == null && d.Id != exceptId)
            .SumAsync(d => (long?)d.AmountRials, ct) ?? 0;

    public static async Task<long> ShopLossRialsAsync(ArteDbContext db, Guid membershipId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        (await LossesInRangeAsync(db, from, to, ct)).Where(l => l.ResponsibleMembershipId == membershipId).Sum(l => l.AmountRials);

    /// <summary>Case-linked losses use the case open time; the rest use when the loss was recorded.</summary>
    public static async Task<List<ShopLossSlice>> LossesInRangeAsync(ArteDbContext db, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var start = from.ToUniversalTime();
        var end = to.ToUniversalTime();
        var unlinked = await db.ShopLossReports.AsNoTracking()
            .Where(l => l.CaseId == null && l.CreatedAt >= start && l.CreatedAt < end)
            .Select(l => new ShopLossSlice(l.Id, l.ResponsibleUserId, l.AmountRials, l.CreatedAt))
            .ToListAsync(ct);
        var linked = await (
            from loss in db.ShopLossReports.AsNoTracking()
            join c in db.Cases.AsNoTracking() on loss.CaseId equals c.Id
            where c.OpenedAt >= start && c.OpenedAt < end
            select new ShopLossSlice(loss.Id, loss.ResponsibleUserId, loss.AmountRials, c.OpenedAt)).ToListAsync(ct);
        unlinked.AddRange(linked);
        return unlinked;
    }
}
