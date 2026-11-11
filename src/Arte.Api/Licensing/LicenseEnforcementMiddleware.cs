using Arte.Api.Security;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Licensing;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Licensing;

/// <summary>
/// Runs after the member and branch are known.
/// <list type="bullet">
/// <item>A branch switched off by the platform admin can do nothing except read /me and sign out (403 branch_inactive).</item>
/// <item>A branch whose licence ended is read-only for growth: no new intake, customers or staff (402 license_expired).
/// Work on existing cases — moving stages, delivering, recording payments — keeps working, so no vehicle or money is stuck.</item>
/// </list>
/// Open-mode (demo) sessions are exempt.
/// </summary>
public sealed class LicenseEnforcementMiddleware(RequestDelegate next)
{
    private static readonly string[] AlwaysAllowed = ["/api/v1/me", "/api/v1/auth/", "/api/v1/license", "/api/v1/client-errors"];

    private static readonly string[] NewThings = ["/api/v1/cases", "/api/v1/customers", "/api/v1/staff"];

    // Routing ignores case, so this check must too (otherwise /API/V1/CASES would slip past it).
    private static bool CreatesSomethingNew(HttpRequest r)
    {
        if (!HttpMethods.IsPost(r.Method) && !HttpMethods.IsPut(r.Method) && !HttpMethods.IsPatch(r.Method)) return false;
        var path = r.Path.Value?.TrimEnd('/') ?? "";
        if (NewThings.Any(p => string.Equals(path, p, StringComparison.OrdinalIgnoreCase))) return true;
        // Photos / videos on a case: POST /api/v1/cases/{id}/attachments
        if (HttpMethods.IsPost(r.Method)
            && path.StartsWith("/api/v1/cases/", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith("/attachments", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public async Task InvokeAsync(HttpContext http, RequestUser user, ArteDbContext db, IClock clock)
    {
        var path = http.Request.Path.Value ?? "";
        if (user.Membership is not { } m || http.User.HasClaim(c => c.Type == ArteClaims.OpenMode)
            || AlwaysAllowed.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await next(http);
            return;
        }

        // Arte support (platform admin inside a branch) must always see the shop, even when it is switched off.
        if (m.IsSupport)
        {
            await next(http);
            return;
        }

        var tenant = await db.Tenants.AsNoTracking().Where(t => t.Id == m.TenantId)
            .Select(t => new { t.IsActive, t.DeactivatedReason }).SingleAsync(http.RequestAborted);
        if (!tenant.IsActive)
        {
            await Results.Problem(statusCode: 403, title: "این شعبه غیرفعال شده است. با پشتیبانی آرته تماس بگیرید.",
                extensions: new Dictionary<string, object?> { ["code"] = "branch_inactive" }).ExecuteAsync(http);
            return;
        }

        if (CreatesSomethingNew(http.Request))
        {
            var access = await LicenseService.AccessAsync(db, m.TenantId, clock.UtcNow, http.RequestAborted);
            if (!access.CanGrow)
            {
                await Results.Problem(statusCode: 402, title: access.LockMessage,
                    extensions: new Dictionary<string, object?> { ["code"] = access.LockCode }).ExecuteAsync(http);
                return;
            }
        }

        await next(http);
    }
}
