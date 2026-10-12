using Arte.Api.Billing;
using Arte.Api.Cases;
using Arte.Api.Licensing;
using Arte.Api.Security;
using Arte.Core.Billing;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Tenancy;
using Arte.Core.Workflows;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Tenants;

/// <summary>
/// Branches and brands. A branch is a business (tenant) with its own data, licence and staff; a brand groups branches.
/// Only the platform admin creates brands and branches; every new branch starts with the free trial.
/// Anyone in more than one branch gets a branches dashboard: each branch's numbers, read inside that branch.
/// </summary>
public static class BranchEndpoints
{
    public sealed record BrandInput(string? Name, bool? IsActive);
    public sealed record BranchInput(
        string? Name, Guid? BrandId, string? NewBrandName, string? BusinessType, string? Phone,
        string? OwnerMobile, string? OwnerName, Guid? CopyFromId, bool? CopySettings, bool? CopyCatalog, bool? CopyStaff);
    public sealed record AssignBrand(Guid? BrandId);

    public static void MapBranches(this IEndpointRouteBuilder app)
    {
        // ── the dashboard of my branches ──
        app.MapGet("/api/v1/branches", async (RequestUser me, ArteDbContext db, IServiceScopeFactory scopes, IClock clock, CancellationToken ct) =>
        {
            var userId = me.RequiredUserId;
            var mine = await (from m in db.Memberships.IgnoreQueryFilters([ArteDbContext.TenantFilter]).AsNoTracking()
                              where m.UserId == userId && m.IsActive
                              join t in db.Tenants on m.TenantId equals t.Id
                              join o in db.Organizations on t.OrganizationId equals o.Id into oj
                              from o in oj.DefaultIfEmpty()
                              orderby o == null ? t.Name : o.Name, t.Name
                              select new { Membership = m, Tenant = t, Brand = o == null ? null : o.Name })
                .ToListAsync(ct);

            var now = clock.UtcNow;
            var dayStart = Arte.Api.Cases.CaseAlerts.TehranMidnightUtc(now);
            var rows = new List<object>();
            foreach (var x in mine)
            {
                // Each branch is read inside its own tenant, with this member's own access rules.
                await using var scope = scopes.CreateAsyncScope();
                var sp = scope.ServiceProvider;
                sp.GetRequiredService<TenantContext>().Set(x.Tenant.Id);
                var bdb = sp.GetRequiredService<ArteDbContext>();
                var m = x.Membership;
                var cases = bdb.Cases.AsNoTracking().Where(c => !c.IsSample);
                if (!CaseAccess.CanSeeAll(m)) cases = cases.Where(c => c.AssigneeId == m.Id);
                var open = await (from c in cases join s in bdb.Stages on c.StageId equals s.Id where !s.IsTerminal
                                  select new { s.Category, c.WaitReason, c.AssigneeId }).ToListAsync(ct);
                var deliveredToday = await (from c in cases join s in bdb.Stages on c.StageId equals s.Id
                                            where s.Key == "delivered" && c.ClosedAt >= dayStart select c.Id).CountAsync(ct);
                var seesMoney = m.Has(Permissions.PaymentsRecord) || m.Has(Permissions.ReportsView);
                long? received = seesMoney
                    ? await (from p in bdb.Payments join c in bdb.Cases on p.CaseId equals c.Id
                             where p.PaidAt >= dayStart && !c.IsSample select (long?)p.AmountRials).SumAsync(ct) ?? 0
                    : null;
                long? credit = null;
                if (seesMoney)
                {
                    var delivered = await (from c in bdb.Cases join s in bdb.Stages on c.StageId equals s.Id
                                           where s.Key == "delivered" && !c.IsSample select c.Id).ToListAsync(ct);
                    credit = (await BillingEndpoints.Balances(bdb, delivered, ct)).Values.Where(v => v > 0).Sum();
                }
                var unread = await bdb.NotificationRecipients.CountAsync(r => r.MembershipId == m.Id && r.ReadAt == null, ct);
                rows.Add(new
                {
                    x.Tenant.Id, x.Tenant.Name, x.Brand, m.Role, x.Tenant.IsActive,
                    License = await LicenseService.StatusAsync(bdb, x.Tenant.Id, now, ct),
                    Open = open.Count,
                    Active = open.Count(o => o.Category is StageCategories.Active or StageCategories.Open && o.WaitReason == null),
                    Waiting = open.Count(o => o.WaitReason != null || o.Category == StageCategories.Waiting),
                    Ready = open.Count(o => o.Category == StageCategories.Done),
                    Unassigned = CaseAccess.CanSeeAll(m) ? open.Count(o => o.AssigneeId == null && o.Category != StageCategories.Done) : (int?)null,
                    OpenedToday = await cases.CountAsync(c => c.OpenedAt >= dayStart, ct),
                    DeliveredToday = deliveredToday,
                    ReceivedTodayRials = received,
                    CreditRials = credit,
                    Unread = unread,
                });
            }
            return Results.Ok(rows);
        }).RequireAuthorization();

        // ── platform admin: brands and branches ──
        var admin = app.MapGroup("/api/v1/admin").RequirePlatformAdmin();

        admin.MapGet("/brands", async (ArteDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Organizations.AsNoTracking().OrderBy(o => o.Name)
                .Select(o => new
                {
                    o.Id, o.Name, o.IsActive, o.CreatedAt,
                    Branches = db.Tenants.Where(t => t.OrganizationId == o.Id).OrderBy(t => t.Name)
                        .Select(t => new { t.Id, t.Name, t.IsActive }).ToList(),
                }).ToListAsync(ct)));

        admin.MapPost("/brands", async (BrandInput req, ArteDbContext db, IClock clock, RequestUser me, Audit audit, CancellationToken ct) =>
        {
            var name = req.Name?.Trim();
            if (name is not { Length: >= 2 and <= 120 })
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["نام برند ۲ تا ۱۲۰ حرف."] });
            var brand = new Organization { Name = name, CreatedAt = clock.UtcNow };
            db.Organizations.Add(brand);
            audit.Record("admin.brand_created", null, me.RequiredUserId, name);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/admin/brands/{brand.Id}", new { brand.Id });
        });

        admin.MapPatch("/brands/{id:guid}", async (Guid id, BrandInput req, ArteDbContext db, CancellationToken ct) =>
        {
            var brand = await db.Organizations.SingleOrDefaultAsync(o => o.Id == id, ct);
            if (brand is null) return Results.NotFound();
            var name = req.Name?.Trim();
            if (req.Name is not null && name is not { Length: >= 2 and <= 120 })
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = ["نام برند ۲ تا ۱۲۰ حرف."] });
            if (name is not null) brand.Name = name;
            if (req.IsActive is { } a) brand.IsActive = a;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // Put an existing business into a brand, or take it out (null).
        admin.MapPatch("/businesses/{id:guid}/brand", async (Guid id, AssignBrand req, ArteDbContext db, CancellationToken ct) =>
        {
            var t = await db.Tenants.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (t is null) return Results.NotFound();
            if (req.BrandId is { } b && !await db.Organizations.AnyAsync(o => o.Id == b, ct)) return Results.NotFound();
            t.OrganizationId = req.BrandId;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        // A new branch: its owner (found or created by mobile), the free trial, and optionally the settings, price list
        // and staff of another branch. Cases, customers and money are never copied.
        admin.MapPost("/branches", async (BranchInput req, ArteDbContext db, IServiceScopeFactory scopes, IClock clock,
            RequestUser me, Audit audit, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            var name = req.Name?.Trim();
            if (name is not { Length: >= 2 and <= 120 }) errors["name"] = ["نام شعبه ۲ تا ۱۲۰ حرف."];
            if (!Mobile.TryNormalize(req.OwnerMobile, out var ownerMobile)) errors["ownerMobile"] = ["موبایل مدیر شعبه معتبر نیست."];
            var ownerName = req.OwnerName?.Trim();
            if (ownerName is { Length: > 80 }) errors["ownerName"] = ["نام حداکثر ۸۰ حرف."];
            var businessType = req.BusinessType ?? BusinessTypes.MotorcycleRepair;
            if (!BusinessTypes.All.Contains(businessType)) errors["businessType"] = ["نوع کسب‌وکار نامعتبر است."];
            var phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim();
            if (phone is { Length: > 20 }) errors["phone"] = ["شماره تلفن معتبر نیست."];
            var newBrand = req.NewBrandName?.Trim();
            if (newBrand is { Length: > 0 and (< 2 or > 120) }) errors["newBrandName"] = ["نام برند ۲ تا ۱۲۰ حرف."];
            if (req.BrandId is { } bid && !await db.Organizations.AnyAsync(o => o.Id == bid, ct)) errors["brandId"] = ["برند پیدا نشد."];
            Tenant? source = null;
            if (req.CopyFromId is { } cid && (source = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(t => t.Id == cid, ct)) is null)
                errors["copyFromId"] = ["شعبه‌ی مبدأ پیدا نشد."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var now = clock.UtcNow;
            Guid? brandId = req.BrandId;
            if (brandId is null && !string.IsNullOrEmpty(newBrand))
            {
                var brand = new Organization { Name = newBrand, CreatedAt = now };
                db.Organizations.Add(brand);
                brandId = brand.Id;
            }
            var owner = await db.Users.SingleOrDefaultAsync(u => u.Mobile == ownerMobile, ct);
            if (owner is null)
            {
                owner = new User { Mobile = ownerMobile, DisplayName = string.IsNullOrEmpty(ownerName) ? null : ownerName, CreatedAt = now };
                db.Users.Add(owner);
            }
            await db.SaveChangesAsync(ct);

            // Same provisioning as sign-up: owner membership, workflow and the free trial licence.
            var tenant = await TenantEndpoints.ProvisionAsync(scopes, owner.Id, name!, phone,
                string.IsNullOrEmpty(ownerName) ? null : ownerName, ct, businessType, brandId);
            if (source is not null) await CopyAsync(scopes, source, tenant.Id, req, now, ct);
            audit.Record("admin.branch_created", tenant.Id, me.RequiredUserId, $"{tenant.Name} owner={Mobile.Mask(ownerMobile)} from={source?.Id}");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/admin/businesses/{tenant.Id}", new { tenant.Id, tenant.Name, BrandId = brandId });
        });
    }

    /// <summary>Copies business settings, the price list and staff (as members of the new branch) from another branch.</summary>
    private static async Task CopyAsync(IServiceScopeFactory scopes, Tenant from, Guid toId, BranchInput req, DateTimeOffset now, CancellationToken ct)
    {
        List<CatalogItem> catalog = [];
        List<Membership> staff = [];
        await using (var read = scopes.CreateAsyncScope())
        {
            read.ServiceProvider.GetRequiredService<TenantContext>().Set(from.Id);
            var rdb = read.ServiceProvider.GetRequiredService<ArteDbContext>();
            if (req.CopyCatalog == true) catalog = await rdb.CatalogItems.AsNoTracking().Where(c => c.IsActive).ToListAsync(ct);
            if (req.CopyStaff == true) staff = await rdb.Memberships.AsNoTracking().Where(m => m.IsActive && m.Role != Roles.Owner).ToListAsync(ct);
        }

        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(toId);
        var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
        var t = await db.Tenants.SingleAsync(x => x.Id == toId, ct);
        if (req.CopySettings == true)
        {
            t.RequireAssigneeOnIntake = from.RequireAssigneeOnIntake;
            t.RequireCustomerApproval = from.RequireCustomerApproval;
            t.RequireFinalReview = from.RequireFinalReview;
            t.BusinessType = from.BusinessType;
            t.VehicleKinds = from.VehicleKinds;
            t.CustomerSmsEnabled = from.CustomerSmsEnabled;
            t.SmsOnOpened = from.SmsOnOpened;
            t.SmsOnReady = from.SmsOnReady;
            t.SmsOnDelivered = from.SmsOnDelivered;
            t.TrackShowStages = from.TrackShowStages;
            t.TrackShowItems = from.TrackShowItems;
            t.TrackShowAmounts = from.TrackShowAmounts;
            t.PhotosVisibleByDefault = from.PhotosVisibleByDefault;
            t.RequireTransferReceipt = from.RequireTransferReceipt;
            t.SurveySendOn = from.SurveySendOn;
            t.SurveyDelayMinutes = from.SurveyDelayMinutes;
            t.SurveyAlertBelow = from.SurveyAlertBelow;
            await WorkflowUpgrader.UpgradeTenantAsync(db, t, ct);
        }
        foreach (var c in catalog)
            db.CatalogItems.Add(new CatalogItem
            {
                Kind = c.Kind, Title = c.Title, DefaultPriceRials = c.DefaultPriceRials, DefaultCostRials = c.DefaultCostRials,
                DefaultWarrantyDays = c.DefaultWarrantyDays, StandardItemId = c.StandardItemId, CreatedAt = now,
            });
        var already = await db.Memberships.Select(m => m.UserId).ToListAsync(ct);
        foreach (var m in staff.Where(s => !already.Contains(s.UserId)))
            db.Memberships.Add(new Membership
            {
                UserId = m.UserId, Role = m.Role, Permissions = m.Permissions, PayModel = m.PayModel,
                CommissionPercent = m.CommissionPercent, FixedMonthlyRials = m.FixedMonthlyRials, CommissionType = m.CommissionType,
                CommissionFixedRials = m.CommissionFixedRials, CommissionBase = m.CommissionBase, SurveyNotify = m.SurveyNotify,
                CreatedAt = now,
            });
        await db.SaveChangesAsync(ct);
    }
}
