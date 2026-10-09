using System.Security.Cryptography;
using System.Text;
using Arte.Api.Options;
using Arte.Api.Security;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Tenancy;
using Arte.Core.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arte.Api.Tenants;

public static class TenantEndpoints
{
    public sealed record CreateTenant(string? Name, string? Phone, string? InviteCode, string? OwnerName);
    public sealed record BusinessSettings(string? Name, string? Phone, string? Address, bool? RequireAssigneeOnIntake,
        bool? RequireCustomerApproval, bool? RequireFinalReview);

    public static void MapTenants(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me", async (RequestUser me, ArteDbContext db, IConfiguration config, IClock clock, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == me.RequiredUserId, ct);
            var m = me.Membership;
            return Results.Ok(new
            {
                user.Id,
                user.Mobile,
                user.DisplayName,
                user.IsPlatformAdmin,
                OpenMode = config.GetValue("Auth:OpenMode", false),
                Business = m is null ? null : new
                {
                    m.TenantId,
                    Name = await db.Tenants.Where(t => t.Id == m.TenantId).Select(t => t.Name).SingleAsync(ct),
                    RequireAssigneeOnIntake = await db.Tenants.Where(t => t.Id == m.TenantId).Select(t => t.RequireAssigneeOnIntake).SingleAsync(ct),
                    m.Role,
                    Permissions = m.Role == Roles.Owner ? [.. Permissions.All] : m.Permissions,
                    IsActive = await db.Tenants.Where(t => t.Id == m.TenantId).Select(t => t.IsActive).SingleAsync(ct),
                    License = await Arte.Api.Licensing.LicenseService.StatusAsync(db, m.TenantId, clock.UtcNow, ct),
                    TourDone = await db.Memberships.Where(x => x.Id == m.Id).Select(x => x.TourDoneAt != null).SingleAsync(ct),
                    SampleCaseId = await db.Cases.Where(c => c.IsSample).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct),
                },
            });
        }).RequireAuthorization();

        app.MapGet("/api/v1/settings/business", async (RequestUser me, ArteDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Tenants.AsNoTracking().Where(t => t.Id == me.RequiredMembership.TenantId)
                .Select(t => new { t.Name, t.Phone, t.Address, t.RequireAssigneeOnIntake, t.RequireCustomerApproval, t.RequireFinalReview }).SingleAsync(ct)))
            .RequirePermission(Permissions.SettingsManage);

        app.MapPut("/api/v1/settings/business", async (BusinessSettings req, RequestUser me, ArteDbContext db, Audit audit, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (req.Name is not null && (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 120)) errors["name"] = ["نام ۱ تا ۱۲۰ حرف."];
            if (req.Phone is { Length: > 20 }) errors["phone"] = ["حداکثر ۲۰ کاراکتر."];
            if (req.Address is { Length: > 300 }) errors["address"] = ["حداکثر ۳۰۰ حرف."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var t = await db.Tenants.SingleAsync(x => x.Id == me.RequiredMembership.TenantId, ct);
            if (req.Name is not null) t.Name = req.Name.Trim();
            if (req.Phone is not null) t.Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim();
            if (req.Address is not null) t.Address = string.IsNullOrWhiteSpace(req.Address) ? null : req.Address.Trim();
            if (req.RequireAssigneeOnIntake is { } r) t.RequireAssigneeOnIntake = r;
            if (req.RequireCustomerApproval is { } ca) t.RequireCustomerApproval = ca;
            if (req.RequireFinalReview is { } fr) t.RequireFinalReview = fr;
            audit.Record("settings.business_updated", t.Id, me.RequiredUserId);
            await db.SaveChangesAsync(ct);
            // Optional workflow steps follow the settings.
            await WorkflowUpgrader.UpgradeTenantAsync(db, t, ct);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { t.Name, t.Phone, t.Address, t.RequireAssigneeOnIntake, t.RequireCustomerApproval, t.RequireFinalReview });
        }).RequirePermission(Permissions.SettingsManage);

        app.MapPost("/api/v1/tenants", async (CreateTenant req, RequestUser me, IServiceScopeFactory scopes,
            IOptions<SignupOptions> signup, ArteDbContext db, CancellationToken ct) =>
        {
            if (signup.Value.RequireInviteCode && !InviteCodeMatches(signup.Value.InviteCode, req.InviteCode))
                return Results.Problem(statusCode: 403, title: "کد دعوت معتبر نیست.");

            var errors = new Dictionary<string, string[]>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == me.RequiredUserId, ct);
            var ownerName = req.OwnerName?.Trim();
            if (string.IsNullOrEmpty(ownerName)) ownerName = user.DisplayName;
            if (string.IsNullOrEmpty(ownerName) || ownerName.Length < 2) errors["ownerName"] = ["نام و نام خانوادگی را بنویسید."];
            else if (ownerName.Length > 80) errors["ownerName"] = ["نام حداکثر ۸۰ حرف."];
            // The business name is optional at sign-up; it can be changed later in Settings.
            var name = req.Name?.Trim();
            // Empty → named after the owner's family name (the last word of the full name); editable in Settings.
            if (string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(ownerName))
                name = $"تعمیرگاه {ownerName.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1]}";
            if (string.IsNullOrEmpty(name) || name.Length > 120) errors["name"] = ["نام کسب‌وکار حداکثر ۱۲۰ حرف."];
            string? phone = null;
            if (!string.IsNullOrWhiteSpace(req.Phone))
            {
                phone = req.Phone.Trim();
                if (phone.Length > 20 || !phone.All(c => char.IsAsciiDigit(c) || c is '-' or ' ' or '+'))
                    errors["phone"] = ["شماره تلفن معتبر نیست."];
            }
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var owned = await db.Memberships.IgnoreQueryFilters()
                .CountAsync(m => m.UserId == me.RequiredUserId && m.Role == Roles.Owner, ct);
            if (owned >= signup.Value.MaxBusinessesPerUser)
                return Results.Problem(statusCode: 403, title: "به سقف تعداد کسب‌وکار رسیده‌اید.");

            var tenant = await ProvisionAsync(scopes, me.RequiredUserId, name!, phone, ownerName, ct);

            return Results.Created($"/api/v1/tenants/{tenant.Id}", new { tenant.Id, tenant.Name });
        }).RequireAuthorization().RequireRateLimiting("auth");
    }

    /// <summary>Creates a business with the motorcycle workflow and makes the user its owner.</summary>
    public static async Task<Tenant> ProvisionAsync(IServiceScopeFactory scopes, Guid ownerUserId, string name,
        string? phone, string? ownerName, CancellationToken ct)
    {
        // A fresh scope so the new tenant becomes the tenant of this unit of work only.
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var clock = sp.GetRequiredService<IClock>();
        var db = sp.GetRequiredService<ArteDbContext>();
        var tenant = new Tenant { Name = name, Phone = phone, Vertical = WorkflowTemplates.MotorcycleRepair, CreatedAt = clock.UtcNow };
        sp.GetRequiredService<TenantContext>().Set(tenant.Id);

        db.Tenants.Add(tenant);
        var owner = new Membership
        {
            TenantId = tenant.Id,
            UserId = ownerUserId,
            Role = Roles.Owner,
            Permissions = Roles.DefaultPermissions(Roles.Owner),
            CreatedAt = clock.UtcNow,
        };
        db.Memberships.Add(owner);
        db.Licenses.Add(Arte.Api.Licensing.LicenseService.Trial(tenant.Id, clock.UtcNow));
        db.Workflows.Add(WorkflowTemplates.Instantiate(WorkflowTemplates.MotorcycleRepair, tenant.Id, tenant));

        if (!string.IsNullOrEmpty(ownerName))
        {
            var user = await db.Users.SingleAsync(u => u.Id == ownerUserId, ct);
            user.DisplayName = ownerName;
        }
        sp.GetRequiredService<Audit>().Record("tenant.created", tenant.Id, ownerUserId, tenant.Name);
        await db.SaveChangesAsync(ct);

        // Every new business starts with the sample case the intro tour walks through.
        if (sp.GetRequiredService<IConfiguration>().GetValue("Onboarding:SampleData", true))
            await Arte.Api.Onboarding.OnboardingEndpoints.EnsureSampleAsync(db, owner, clock, ct);
        return tenant;
    }

    private static bool InviteCodeMatches(string expected, string? presented)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(presented)) return false;
        var a = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var b = SHA256.HashData(Encoding.UTF8.GetBytes(presented.Trim()));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}
