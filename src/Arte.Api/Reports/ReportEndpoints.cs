using Arte.Api.Billing;
using Arte.Api.Security;
using Arte.Core.Billing;
using Arte.Core.Cases;
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
        app.MapGet("/api/v1/reports/workshop", WorkshopAsync).RequirePermission(Permissions.ReportsView);
    }

    /// <summary>
    /// The workshop at a glance: today, each person's work in the period (jobs finished, customers' satisfaction), and the most frequent
    /// problems (reported at intake and found later). The people list is for whoever sees reports (master and owner).
    /// </summary>
    private static async Task<IResult> WorkshopAsync(DateTimeOffset from, DateTimeOffset to, DateTimeOffset? dayFrom, DateTimeOffset? dayTo,
        ArteDbContext db, CancellationToken ct)
    {
        from = from.ToUniversalTime();
        to = to.ToUniversalTime();
        if (to <= from || to - from > TimeSpan.FromDays(400))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["to"] = ["بازه نامعتبر است."] });
        var dFrom = (dayFrom ?? DateTimeOffset.UtcNow.AddHours(-24)).ToUniversalTime();
        var dTo = (dayTo ?? dFrom.AddDays(1)).ToUniversalTime();
        if (dTo <= dFrom || dTo - dFrom > TimeSpan.FromDays(2))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["dayTo"] = ["بازه‌ی امروز نامعتبر است."] });

        // Today: what came in and what went out, and how many cases sit in each status right now.
        var openedToday = await db.Cases.CountAsync(c => c.OpenedAt >= dFrom && c.OpenedAt < dTo && !c.IsSample, ct);
        var deliveredToday = await (from c in db.Cases join s in db.Stages on c.StageId equals s.Id
                                    where s.Key == "delivered" && c.ClosedAt >= dFrom && c.ClosedAt < dTo && !c.IsSample select c.Id).CountAsync(ct);
        var byStatus = await (from c in db.Cases join s in db.Stages on c.StageId equals s.Id
                              where !s.IsTerminal && !c.IsSample
                              group s by new { s.Key, s.Name, s.Order } into g
                              orderby g.Key.Order
                              select new { g.Key.Key, g.Key.Name, Count = g.Count() }).ToListAsync(ct);

        // People: who moved a case out of repairing (finished it) in the period, and how satisfied the customers were.
        var moves = await db.CaseEvents.AsNoTracking()
            .Where(e => e.Type == CaseEventTypes.StageChanged && e.ActorUserId != null && e.OccurredAt >= from && e.OccurredAt < to)
            .Select(e => new { e.CaseId, Actor = e.ActorUserId!.Value, e.Data }).ToListAsync(ct);
        string? Prop(System.Text.Json.JsonDocument? d, string name) =>
            d?.RootElement.TryGetProperty(name, out var v) == true && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;
        var finished = moves.Where(e =>
        {
            var fromKey = Prop(e.Data, "fromKey") ?? (Prop(e.Data, "from") == "در حال تعمیر" ? "repairing" : null);
            var toKey = Prop(e.Data, "toKey") ?? Prop(e.Data, "to") switch { "بررسی نهایی استاد" => "review", "آماده تحویل" => "ready", _ => null };
            return fromKey == "repairing" && toKey is "review" or "ready";
        }).Select(e => (e.CaseId, e.Actor)).Distinct().ToList();

        var invites = await db.SurveyInvites.AsNoTracking().Where(i => i.AnsweredAt >= from && i.AnsweredAt < to && i.Score != null).ToListAsync(ct);
        var members = await db.Memberships.AsNoTracking().Include(m => m.User).Where(m => m.IsActive).OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var people = members.Select(m =>
        {
            var mine = invites.Where(i => i.ResponsibleIds.Contains(m.Id)).ToList();
            return new
            {
                MembershipId = m.Id, Name = m.DisplayName ?? m.User!.Mobile, m.Role,
                Finished = finished.Count(f => f.Actor == m.UserId),
                Surveys = mine.Count,
                AverageScore = mine.Count == 0 ? (decimal?)null : Math.Round(mine.Average(i => i.Score!.Value), 1),
            };
        }).Where(p => p.Finished > 0 || p.Surveys > 0).OrderByDescending(p => p.Finished).ToList();

        // Most frequent problems: what customers reported at intake, and what the workshop found on the cases.
        var reported = (await db.Cases.AsNoTracking().Where(c => c.OpenedAt >= from && c.OpenedAt < to && !c.IsSample).Select(c => c.ReportedProblems).ToListAsync(ct))
            .SelectMany(x => x).GroupBy(x => x).Select(g => new { Title = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(10).ToList();
        var found = (await db.CaseFindings.AsNoTracking().Where(f => f.CreatedAt >= from && f.CreatedAt < to).Select(f => new { f.Title, f.Source }).ToListAsync(ct))
            .GroupBy(f => f.Title).Select(g => new { Title = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(10).ToList();
        var motorTypes = await db.Cases.AsNoTracking().Where(c => c.OpenedAt >= from && c.OpenedAt < to && !c.IsSample && c.MotorType != null)
            .GroupBy(c => c.MotorType!).Select(g => new { Type = g.Key, Count = g.Count() }).ToListAsync(ct);

        return Results.Ok(new
        {
            From = from, To = to,
            Today = new { Opened = openedToday, Delivered = deliveredToday, ByStatus = byStatus },
            People = people,
            ReportedProblems = reported, FoundProblems = found, MotorTypes = motorTypes,
        });
    }

    private static async Task<IResult> SummaryAsync(DateTimeOffset from, DateTimeOffset to, ArteDbContext db, Arte.Api.Security.RequestUser me, CancellationToken ct)
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
        // What was sold most: goods and work of the delivered cases, by name (one name = one row in the catalog).
        var isOwner = me.RequiredMembership.Role == Roles.Owner;
        var topItems = billable.GroupBy(i => (Kind: i.Kind == ItemKinds.Part ? ItemKinds.Part : ItemKinds.Labor, i.Title))
            .Select(g => new
            {
                g.Key.Kind, g.Key.Title,
                Cases = g.Select(i => i.CaseId).Distinct().Count(),
                Quantity = g.Sum(i => i.Quantity),
                SalesRials = g.Sum(i => i.LineTotalRials),
                ProfitRials = isOwner && g.Key.Kind == ItemKinds.Part && g.All(i => i.UnitCostRials != null)
                    ? g.Sum(i => i.LineTotalRials - i.LineCostRials) : (long?)null,
            })
            .OrderByDescending(x => x.SalesRials).Take(15).ToList();
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
                Name = m.DisplayName ?? m.User!.Mobile,
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
            SalesRials = sales, PartsRials = parts, WorkRials = work,
            // Profit is the owner's alone.
            PartsProfitRials = me.RequiredMembership.Role == Roles.Owner ? partsProfit : (long?)null,
            ReceivedRials = received, ReceivablesRials = receivables,
            Staff = staff,
            TopItems = topItems,
            Survey = await Arte.Api.Surveys.SurveyEndpoints.ReportAsync(db, me.RequiredMembership.TenantId, from, to, ct),
        });
    }
}
