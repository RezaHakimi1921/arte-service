using Arte.Api.Security;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Licensing;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Licensing;

/// <summary>
/// The branch's own view of its subscription (owner/managers) and the platform admin panel
/// (businesses, licences, plans). Admin endpoints work across businesses, so they bypass the tenant filter
/// by name only (<see cref="ArteDbContext.TenantFilter"/>); soft-delete filtering stays on.
/// </summary>
public static class LicenseEndpoints
{
    public sealed record GrantLicense(string? Kind, Guid? PlanId, int? Months, int? Days, long? PriceRials, string? Note);
    public sealed record UpdateBusiness(bool? IsActive, string? Reason);
    public sealed record PlanInput(string? Name, int? Months, long? PriceRials, bool? IsActive, int? SortOrder);

    public static void MapLicensing(this IEndpointRouteBuilder app)
    {
        // ───────── the branch's own subscription ─────────
        app.MapGet("/api/v1/license", async (RequestUser me, ArteDbContext db, IClock clock, IConfiguration config, CancellationToken ct) =>
        {
            var tenantId = me.RequiredMembership.TenantId;
            var status = await LicenseService.StatusAsync(db, tenantId, clock.UtcNow, ct);
            var history = await db.Licenses.AsNoTracking().Where(l => l.TenantId == tenantId).OrderByDescending(l => l.EndsAt)
                .Select(l => new { l.Id, l.Kind, l.StartsAt, l.EndsAt, Plan = db.Plans.Where(p => p.Id == l.PlanId).Select(p => p.Name).FirstOrDefault() })
                .Take(50).ToListAsync(ct);
            var plans = await db.Plans.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder)
                .Select(p => new { p.Id, p.Name, p.Months, p.PriceRials }).ToListAsync(ct);
            return Results.Ok(new { Status = status, History = history, Plans = plans, SupportPhone = config["Platform:SupportPhone"] });
        }).RequirePermission(Permissions.SettingsManage);

        // ───────── platform admin panel ─────────
        var admin = app.MapGroup("/api/v1/admin").RequireAuthorization().AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var me = http.RequestServices.GetRequiredService<RequestUser>();
            var db = http.RequestServices.GetRequiredService<ArteDbContext>();
            var isAdmin = me.UserId is { } uid && await db.Users.AnyAsync(u => u.Id == uid && u.IsPlatformAdmin, http.RequestAborted);
            return isAdmin ? await next(ctx) : Results.Problem(statusCode: 403, title: "این بخش فقط برای مدیریت آرته است.");
        });

        admin.MapGet("/businesses", async (string? q, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var now = clock.UtcNow;
            // Across businesses: only the tenant filter is bypassed (by name); soft delete stays on.
            var allMembers = db.Memberships.IgnoreQueryFilters([ArteDbContext.TenantFilter]);
            var allCases = db.Cases.IgnoreQueryFilters([ArteDbContext.TenantFilter]);
            var tenants = db.Tenants.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim();
                var byOwner = allMembers
                    .Where(m => m.User!.Mobile.Contains(term) || (m.User.DisplayName != null && m.User.DisplayName.Contains(term)))
                    .Select(m => m.TenantId);
                tenants = tenants.Where(t => t.Name.Contains(term) || (t.Phone != null && t.Phone.Contains(term)) || byOwner.Contains(t.Id));
            }

            var rows = await tenants.OrderByDescending(t => t.CreatedAt).Take(300).Select(t => new
            {
                t.Id, t.Name, t.Phone, t.CreatedAt, t.IsActive,
                Owner = allMembers
                    .Where(m => m.TenantId == t.Id && m.Role == Roles.Owner).OrderBy(m => m.CreatedAt)
                    .Select(m => new { m.User!.DisplayName, m.User.Mobile }).FirstOrDefault(),
                Staff = allMembers.Count(m => m.TenantId == t.Id && m.IsActive),
                Cases = allCases.Count(c => c.TenantId == t.Id && !c.IsSample),
                LastCaseAt = allCases.Where(c => c.TenantId == t.Id && !c.IsSample)
                    .Max(c => (DateTimeOffset?)c.OpenedAt),
                License = db.Licenses.Where(l => l.TenantId == t.Id).OrderByDescending(l => l.EndsAt)
                    .Select(l => new { l.Kind, l.EndsAt }).FirstOrDefault(),
            }).ToListAsync(ct);

            return Results.Ok(rows.Select(r =>
            {
                var (state, days) = LicensePolicy.StateOf(r.License?.EndsAt, now);
                return new
                {
                    r.Id, r.Name, r.Phone, r.CreatedAt, r.IsActive, r.Owner, r.Staff, r.Cases, r.LastCaseAt,
                    License = new LicenseStatus(state, days, r.License?.EndsAt, r.License?.Kind),
                };
            }));
        });

        admin.MapGet("/businesses/{id:guid}", async (Guid id, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var t = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            var licenses = await db.Licenses.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
                .Where(l => l.TenantId == id).OrderByDescending(l => l.CreatedAt)
                .Select(l => new
                {
                    l.Id, l.Kind, l.StartsAt, l.EndsAt, l.PriceRials, l.Note, l.CreatedAt, Revoked = l.DeletedAt != null,
                    Plan = db.Plans.Where(p => p.Id == l.PlanId).Select(p => p.Name).FirstOrDefault(),
                })
                .ToListAsync(ct);
            var members = await db.Memberships.IgnoreQueryFilters([ArteDbContext.TenantFilter]).AsNoTracking()
                .Where(m => m.TenantId == id).OrderBy(m => m.CreatedAt)
                .Select(m => new { m.Role, m.IsActive, m.User!.DisplayName, m.User.Mobile, m.User.LastLoginAt }).ToListAsync(ct);
            return Results.Ok(new
            {
                t.Id, t.Name, t.Phone, t.Address, t.CreatedAt, t.IsActive, t.DeactivatedReason,
                Status = await LicenseService.StatusAsync(db, id, clock.UtcNow, ct),
                Licenses = licenses, Members = members,
            });
        });

        // What a business does with Arte: counts, money and its latest cases (read-only).
        // Runs in a scope switched to that business, so the usual tenant-filtered queries and money rules apply.
        admin.MapGet("/businesses/{id:guid}/activity", async (Guid id, IServiceScopeFactory scopes, IClock clock, CancellationToken ct) =>
        {
            await using var scope = scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ArteDbContext>();
            if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
            sp.GetRequiredService<Arte.Core.Tenancy.TenantContext>().Set(id);

            var now = clock.UtcNow;
            var monthAgo = now.AddDays(-30);
            var cases = db.Cases.AsNoTracking().Where(c => !c.IsSample);
            var rows = await (from c in cases
                              join st in db.Stages on c.StageId equals st.Id
                              select new { c.Id, c.OpenedAt, st.IsTerminal, st.Key }).ToListAsync(ct);
            var delivered = rows.Where(r => r.Key == "delivered").Select(r => r.Id).ToList();
            var balances = await Arte.Api.Billing.BillingEndpoints.Balances(db, delivered, ct);
            var recent = await (from c in cases
                                join st in db.Stages on c.StageId equals st.Id
                                join cu in db.Customers on c.CustomerId equals cu.Id
                                join a in db.Assets on c.AssetId equals a.Id into aj
                                from a in aj.DefaultIfEmpty()
                                orderby c.OpenedAt descending
                                select new { c.Number, Customer = cu.FullName, Vehicle = a == null ? null : a.Title, Stage = st.Name, c.OpenedAt })
                .Take(15).ToListAsync(ct);

            return Results.Ok(new
            {
                Customers = await db.Customers.CountAsync(c => !c.IsSample, ct),
                Cases = rows.Count,
                OpenCases = rows.Count(r => !r.IsTerminal),
                Delivered = delivered.Count,
                CasesLast30Days = rows.Count(r => r.OpenedAt >= monthAgo),
                ReceivedLast30DaysRials = await db.Payments.Where(p => p.PaidAt >= monthAgo).SumAsync(p => (long?)p.AmountRials, ct) ?? 0,
                ReceivablesRials = balances.Values.Where(v => v > 0).Sum(),
                Recent = recent,
            });
        });

        admin.MapPatch("/businesses/{id:guid}", async (Guid id, UpdateBusiness req, RequestUser me, ArteDbContext db, Audit audit, CancellationToken ct) =>
        {
            var t = await db.Tenants.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            if (req.Reason is { Length: > 300 })
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["حداکثر ۳۰۰ حرف."] });
            if (req.IsActive is { } active && active != t.IsActive)
            {
                t.IsActive = active;
                t.DeactivatedReason = active ? null : string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason.Trim();
                audit.Record(active ? "admin.branch_activated" : "admin.branch_deactivated", id, me.RequiredUserId, t.DeactivatedReason);
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        admin.MapPost("/businesses/{id:guid}/licenses", async (Guid id, GrantLicense req, RequestUser me, ArteDbContext db, IClock clock, Audit audit, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
            var errors = new Dictionary<string, string[]>();
            var kind = req.Kind ?? LicenseKinds.Paid;
            if (kind is not (LicenseKinds.Paid or LicenseKinds.Gift)) errors["kind"] = ["نوع باید خرید یا هدیه باشد."];
            Plan? plan = null;
            if (req.PlanId is { } planId)
            {
                plan = await db.Plans.AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, ct);
                if (plan is null) errors["planId"] = ["پلن پیدا نشد."];
            }
            if (kind == LicenseKinds.Paid && req.PlanId is null) errors["planId"] = ["برای خرید، پلن را انتخاب کنید."];
            var months = plan?.Months ?? req.Months ?? 0;
            var days = req.Days ?? 0;
            if (plan is null && (months is < 0 or > 36 || days is < 0 or > 366 || months + days == 0))
                errors["months"] = ["مدت را مشخص کنید (تا ۳۶ ماه یا ۳۶۶ روز)."];
            var price = req.PriceRials ?? (kind == LicenseKinds.Paid ? plan?.PriceRials ?? 0 : 0);
            if (price is < 0 or > 100_000_000_000) errors["priceRials"] = ["مبلغ نامعتبر است."];
            if (req.Note is { Length: > 500 }) errors["note"] = ["یادداشت حداکثر ۵۰۰ حرف."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var now = clock.UtcNow;
            var latest = await LicenseService.LatestAsync(db, id, ct);
            var start = LicensePolicy.NextStart(latest?.EndsAt, now);
            var license = new License
            {
                TenantId = id, Kind = kind, PlanId = plan?.Id, StartsAt = start, EndsAt = start.AddMonths(months).AddDays(days),
                PriceRials = price, Note = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim(),
                CreatedBy = me.RequiredUserId, CreatedAt = now,
            };
            db.Licenses.Add(license);
            audit.Record("admin.license_granted", id, me.RequiredUserId, $"{kind} {plan?.Name ?? $"{months}m{days}d"} → {license.EndsAt:yyyy-MM-dd}");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/admin/licenses/{license.Id}", new { license.Id, license.StartsAt, license.EndsAt });
        });

        // Revoke a wrong grant (soft delete); later licences are not moved.
        admin.MapDelete("/licenses/{id:guid}", async (Guid id, RequestUser me, ArteDbContext db, IClock clock, Audit audit, CancellationToken ct) =>
        {
            var l = await db.Licenses.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (l is null) return Results.NotFound();
            l.DeletedAt = clock.UtcNow;
            l.DeletedBy = me.RequiredUserId;
            audit.Record("admin.license_revoked", l.TenantId, me.RequiredUserId, $"{l.Kind} → {l.EndsAt:yyyy-MM-dd}");
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        admin.MapGet("/plans", async (ArteDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Plans.AsNoTracking().OrderBy(p => p.SortOrder).ThenBy(p => p.Months).ToListAsync(ct)));

        admin.MapPost("/plans", async (PlanInput req, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            if (Validate(req, creating: true) is { } problem) return problem;
            var now = clock.UtcNow;
            var plan = new Plan
            {
                Name = req.Name!.Trim(), Months = req.Months!.Value, PriceRials = req.PriceRials!.Value,
                IsActive = req.IsActive ?? true, SortOrder = req.SortOrder ?? req.Months!.Value, CreatedAt = now, UpdatedAt = now,
            };
            db.Plans.Add(plan);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/admin/plans/{plan.Id}", plan);
        });

        admin.MapPatch("/plans/{id:guid}", async (Guid id, PlanInput req, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var plan = await db.Plans.SingleOrDefaultAsync(p => p.Id == id, ct);
            if (plan is null) return Results.NotFound();
            if (Validate(req, creating: false) is { } problem) return problem;
            if (req.Name is not null) plan.Name = req.Name.Trim();
            if (req.Months is { } months) plan.Months = months;
            if (req.PriceRials is { } price) plan.PriceRials = price;
            if (req.IsActive is { } active) plan.IsActive = active;
            if (req.SortOrder is { } order) plan.SortOrder = order;
            plan.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.Ok(plan);
        });
    }

    private static IResult? Validate(PlanInput req, bool creating)
    {
        var errors = new Dictionary<string, string[]>();
        if ((creating || req.Name is not null) && (string.IsNullOrWhiteSpace(req.Name) || req.Name.Trim().Length > 60))
            errors["name"] = ["نام پلن لازم است (حداکثر ۶۰ حرف)."];
        if ((creating || req.Months is not null) && req.Months is not (>= 1 and <= 36)) errors["months"] = ["مدت بین ۱ تا ۳۶ ماه."];
        if ((creating || req.PriceRials is not null) && req.PriceRials is not (>= 0 and <= 100_000_000_000)) errors["priceRials"] = ["مبلغ نامعتبر است."];
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }
}
