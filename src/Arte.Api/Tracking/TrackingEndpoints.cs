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
    /// <summary>Tracking codes are 6–16 lower-case letters and digits; anything else is not looked up.</summary>
    public static bool ValidCode(string code) =>
        code.Length is >= 6 and <= 16 && code.All(ch => char.IsAsciiLetterLower(ch) || char.IsAsciiDigit(ch));

    public static void MapTracking(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/track/{code}", async (string code, IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            if (!ValidCode(code)) return Results.NotFound();

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

            // Only the photos the shop marked for the customer.
            var photos = await db.CaseAttachments.AsNoTracking().Where(a => a.CaseId == c.Id && a.VisibleToCustomer)
                .OrderBy(a => a.CreatedAt).Select(a => new { a.Id, a.StageKey, a.Caption, a.CreatedAt }).ToListAsync(ct);

            // The satisfaction survey, once the vehicle is delivered (the SMS links here too).
            object? survey = null;
            if (t.SurveyEnabled
                && await db.SurveyInvites.AsNoTracking().SingleOrDefaultAsync(x => x.CaseId == c.Id, ct) is { } invite)
            {
                var state = Arte.Api.Surveys.SurveyService.State(invite, sp.GetRequiredService<Arte.Core.Common.IClock>().UtcNow);
                if (state != "closed")
                    survey = new
                    {
                        State = state,
                        Questions = state == "open"
                            ? (await Arte.Api.Surveys.SurveyService.QuestionsAsync(db, t.Id, ct)).Select(q => new { q.Id, q.Text }).ToList()
                            : null,
                        Score = state == "answered" ? invite.Score : null,
                    };
            }

            return Results.Ok(new
            {
                Shop = new { t.Name, t.Phone, t.Address },
                Survey = survey,
                Photos = photos,
                Case = new
                {
                    Customer = customer.FullName, c.OpenedAt, c.PromisedAt, c.ClosedAt, c.WarrantyUntil,
                    Vehicle = asset is null ? null : new { asset.Title, asset.Kind, asset.Identifier },
                    c.ReportedProblems, c.RequestedServices,
                },
                Status = new { current.Name, current.Key, current.Category, c.WaitReason, c.StageEnteredAt },
                Stages = timeline,
                Items = items,
                Money = money,
            });
        }).AllowAnonymous().RequireRateLimiting("session");

        // A photo the shop shared, served only through its case's tracking code.
        app.MapGet("/api/v1/track/{code}/photos/{photoId:guid}", async (string code, Guid photoId, IServiceScopeFactory scopes,
            IConfiguration config, IHostEnvironment env, CancellationToken ct) =>
        {
            if (!ValidCode(code)) return Results.NotFound();
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
            var photo = await (from a in db.CaseAttachments.IgnoreQueryFilters([ArteDbContext.TenantFilter])
                               join c in db.Cases.IgnoreQueryFilters([ArteDbContext.TenantFilter]) on a.CaseId equals c.Id
                               where a.Id == photoId && a.VisibleToCustomer && c.TrackingCode == code && !c.IsSample
                                     && a.DeletedAt == null && c.DeletedAt == null
                               select new { a.StoragePath, a.ContentType }).SingleOrDefaultAsync(ct);
            if (photo is null) return Results.NotFound();
            var root = Arte.Api.Cases.AttachmentEndpoints.Root(config, env);
            var full = Path.GetFullPath(Path.Combine(root, photo.StoragePath));
            if (!full.StartsWith(root, StringComparison.Ordinal) || !File.Exists(full)) return Results.NotFound();
            return Results.File(full, photo.ContentType);
        }).AllowAnonymous().RequireRateLimiting("session");
    }
}
