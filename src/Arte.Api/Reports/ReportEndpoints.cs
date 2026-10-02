using Arte.Api.Billing;
using Arte.Api.Security;
using Arte.Core.Billing;
using Arte.Core.Data;
using Arte.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Reports;

/// <summary>A handful of fixed, simple reports for the owner (spec 08 R7). Periods are chosen in Jalali on the client.</summary>
public static class ReportEndpoints
{
    public static void MapReports(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/reports/summary", SummaryAsync).RequirePermission(Permissions.ReportsView);
    }

    private static async Task<IResult> SummaryAsync(DateTimeOffset from, DateTimeOffset to, ArteDbContext db, CancellationToken ct)
    {
        from = from.ToUniversalTime();
        to = to.ToUniversalTime();
        if (to <= from || to - from > TimeSpan.FromDays(400))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["بازه نامعتبر است."] });

        var opened = await db.Cases.CountAsync(c => c.OpenedAt >= from && c.OpenedAt < to, ct);
        var delivered = await (from c in db.Cases.AsNoTracking()
                               join s in db.Stages on c.StageId equals s.Id
                               where s.Key == "delivered" && c.ClosedAt >= @from && c.ClosedAt < to
                               select new { c.Id, c.AssigneeId }).ToListAsync(ct);
        var ids = delivered.Select(d => d.Id).ToList();
        var items = await db.CaseItems.AsNoTracking().Where(i => ids.Contains(i.CaseId)).ToListAsync(ct);
        var byCase = items.ToLookup(i => i.CaseId);

        var billable = items.Where(i => i.Billable).ToList();
        var sales = billable.Sum(i => i.LineTotalRials);
        var parts = billable.Where(i => i.Kind == ItemKinds.Part).Sum(i => i.LineTotalRials);
        var work = billable.Where(i => i.Kind is ItemKinds.Labor or ItemKinds.Service).Sum(i => i.LineTotalRials);
        var partsProfit = billable.Where(i => i.Kind == ItemKinds.Part && i.UnitCostRials != null)
            .Sum(i => i.LineTotalRials - i.LineCostRials);
        var received = await db.Payments.Where(p => p.PaidAt >= from && p.PaidAt < to).SumAsync(p => (long?)p.AmountRials, ct) ?? 0;

        // Current receivables (not period-bound): delivered cases still owing.
        var allDelivered = await (from c in db.Cases join s in db.Stages on c.StageId equals s.Id where s.Key == "delivered" select c.Id).ToListAsync(ct);
        var receivables = (await BillingEndpoints.Balances(db, allDelivered, ct)).Values.Where(v => v > 0).Sum();

        // Staff pay: commission from delivered cases they were responsible for, plus their fixed monthly salary.
        var members = await db.Memberships.AsNoTracking().Include(m => m.User).Where(m => m.IsActive).OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var staff = members.Select(m =>
        {
            var mine = delivered.Where(d => d.AssigneeId == m.Id).Select(d => Commission.For(byCase[d.Id], m)).ToList();
            return new
            {
                MembershipId = m.Id,
                Name = m.User!.DisplayName ?? m.User.Mobile,
                m.Role,
                Cases = mine.Count,
                BaseRials = mine.Sum(x => x.BaseRials),
                CommissionRials = mine.Sum(x => x.CommissionRials),
                m.CommissionType, m.CommissionPercent, m.CommissionFixedRials, m.CommissionBase,
                FixedMonthlyRials = m.FixedMonthlyRials ?? 0,
            };
        }).ToList();

        return Results.Ok(new
        {
            From = from, To = to,
            Opened = opened, Delivered = delivered.Count,
            SalesRials = sales, PartsRials = parts, WorkRials = work, PartsProfitRials = partsProfit,
            ReceivedRials = received, ReceivablesRials = receivables,
            Staff = staff,
        });
    }
}
