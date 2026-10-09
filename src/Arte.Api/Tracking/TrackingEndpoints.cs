using Arte.Api.Billing;
using Arte.Core.Billing;
using Arte.Core.Cases;
using Arte.Core.Data;
using Arte.Core.Tenancy;
using Arte.Core.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Tracking;

/// <summary>
/// The customer's read-only view of one case, opened from the SMS link (/t/{code}) without signing in.
/// The random code is the only key. What is shown follows the business's settings; the status, the promise
/// and the shop's contact are always shown. No mobile numbers, costs, notes or staff details leave here.
/// </summary>
public static class TrackingEndpoints
{
    public static void MapTracking(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/track/{code}", async (string code, IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            if (code.Length is < 6 or > 16 || !code.All(ch => char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch)))
                return Results.NotFound();

            await using var scope = scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ArteDbContext>();
            // Find the business by the code (tenant filter bypassed by name only), then read as that business.
            var found = await db.Cases.IgnoreQueryFilters([ArteDbContext.TenantFilter]).AsNoTracking()
                .Where(c => c.TrackingCode == code && !c.IsSample).Select(c => new { c.Id, c.TenantId }).SingleOrDefaultAsync(ct);
            if (found is null) return Results.NotFound();
            sp.GetRequiredService<TenantContext>().Set(found.TenantId);

            var t = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == found.TenantId, ct);
            if (!t.IsActive) return Results.NotFound();
            var c = await db.Cases.AsNoTracking().SingleAsync(x => x.Id == found.Id, ct);
            var customer = await db.Customers.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking().SingleAsync(x => x.Id == c.CustomerId, ct);
            var asset = c.AssetId is null ? null : await db.Assets.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == c.AssetId, ct);
            var stages = await db.Stages.AsNoTracking().Where(s => s.WorkflowId == c.WorkflowId && s.IsActive)
                .OrderBy(s => s.Order).ToListAsync(ct);
            var current = stages.Single(s => s.Id == c.StageId);

            object? timeline = null;
            if (t.TrackShowStages)
            {
                // When each stage was first reached, from the case timeline.
                var events = await db.CaseEvents.AsNoTracking().Where(e => e.CaseId == c.Id && e.Type == CaseEventTypes.StageChanged)
                    .OrderBy(e => e.Id).Select(e => new { e.OccurredAt, e.Data }).ToListAsync(ct);
                var reached = new Dictionary<string, DateTimeOffset>();
                foreach (var e in events)
                    if (e.Data?.RootElement.TryGetProperty("to", out var to) == true && to.GetString() is { } name)
                        reached.TryAdd(name, e.OccurredAt);
                var path = stages.Where(s => s.Category != StageCategories.Cancelled
                                             && (s.Category != StageCategories.Waiting || s.Id == current.Id)).ToList();
                timeline = path.Select(s => new
                {
                    s.Name, s.Key,
                    State = s.Id == current.Id ? "current" : s.Order < current.Order ? "done" : "todo",
                    At = s.Order == stages[0].Order ? c.OpenedAt : reached.TryGetValue(s.Name, out var at) ? at : (DateTimeOffset?)null,
                }).ToList();
            }

            object? items = null, money = null;
            if (t.TrackShowItems || t.TrackShowAmounts)
            {
                var all = await db.CaseItems.AsNoTracking().Where(i => i.CaseId == c.Id).OrderBy(i => i.AddedAt).ToListAsync(ct);
                if (t.TrackShowItems)
                    items = all.Select(i => new
                    {
                        i.Kind, i.Title, i.Quantity, i.Status, i.Supplier,
                        LineTotalRials = t.TrackShowAmounts ? i.LineTotalRials : (long?)null,
                    }).ToList();
                if (t.TrackShowAmounts)
                {
                    var payments = await db.Payments.AsNoTracking().Where(p => p.CaseId == c.Id).ToListAsync(ct);
                    var m = CaseMoney.Of(all, payments);
                    money = new { m.TotalRials, m.PaidRials, m.BalanceRials };
                }
            }

            return Results.Ok(new
            {
                Shop = new { t.Name, t.Phone, t.Address },
                Case = new
                {
                    c.Number, Customer = customer.FullName, c.OpenedAt, c.PromisedAt, c.ClosedAt, c.WarrantyUntil,
                    Vehicle = asset is null ? null : new { asset.Title, asset.Kind, asset.Identifier },
                    c.ReportedProblems, c.RequestedServices,
                },
                Status = new { current.Name, current.Key, current.Category, c.WaitReason, c.StageEnteredAt },
                Stages = timeline,
                Items = items,
                Money = money,
            });
        }).AllowAnonymous().RequireRateLimiting("session");
    }
}
