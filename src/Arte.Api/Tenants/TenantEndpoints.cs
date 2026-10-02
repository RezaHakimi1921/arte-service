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

    public static void MapTenants(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/me", async (RequestUser me, ArteDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == me.RequiredUserId, ct);
            var m = me.Membership;
            return Results.Ok(new
            {
                user.Id,
                user.Mobile,
                user.DisplayName,
                OpenMode = config.GetValue("Auth:OpenMode", false),
                Business = m is null ? null : new
                {
                    m.TenantId,
                    Name = await db.Tenants.Where(t => t.Id == m.TenantId).Select(t => t.Name).SingleAsync(ct),
                    m.Role,
                    Permissions = m.Role == Roles.Owner ? [.. Permissions.All] : m.Permissions,
                },
            });
        }).RequireAuthorization();

        app.MapPost("/api/v1/tenants", async (CreateTenant req, RequestUser me, IServiceScopeFactory scopes,
            IOptions<SignupOptions> signup, ArteDbContext db, CancellationToken ct) =>
        {
            if (!InviteCodeMatches(signup.Value.InviteCode, req.InviteCode))
                return Results.Problem(statusCode: 403, title: "کد دعوت معتبر نیست.");

            var errors = new Dictionary<string, string[]>();
            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > 120) errors["name"] = ["نام کسب‌وکار لازم است (حداکثر ۱۲۰ حرف)."];
            string? phone = null;
            if (!string.IsNullOrWhiteSpace(req.Phone))
            {
                phone = req.Phone.Trim();
                if (phone.Length > 20 || !phone.All(c => char.IsAsciiDigit(c) || c is '-' or ' ' or '+'))
                    errors["phone"] = ["شماره تلفن معتبر نیست."];
            }
            var ownerName = req.OwnerName?.Trim();
            if (ownerName is { Length: > 80 }) errors["ownerName"] = ["نام حداکثر ۸۰ حرف."];
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
        db.Memberships.Add(new Membership
        {
            TenantId = tenant.Id,
            UserId = ownerUserId,
            Role = Roles.Owner,
            Permissions = Roles.DefaultPermissions(Roles.Owner),
            CreatedAt = clock.UtcNow,
        });
        db.Workflows.Add(WorkflowTemplates.Instantiate(WorkflowTemplates.MotorcycleRepair, tenant.Id));

        if (!string.IsNullOrEmpty(ownerName))
        {
            var user = await db.Users.SingleAsync(u => u.Id == ownerUserId, ct);
            user.DisplayName ??= ownerName;
        }
        sp.GetRequiredService<Audit>().Record("tenant.created", tenant.Id, ownerUserId, tenant.Name);
        await db.SaveChangesAsync(ct);
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
