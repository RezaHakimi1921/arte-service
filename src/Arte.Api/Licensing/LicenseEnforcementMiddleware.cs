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

    private static bool CreatesSomethingNew(HttpRequest r) =>
        HttpMethods.IsPost(r.Method) && r.Path.Value?.TrimEnd('/') is "/api/v1/cases" or "/api/v1/customers" or "/api/v1/staff";

    public async Task InvokeAsync(HttpContext http, RequestUser user, ArteDbContext db, IClock clock)
    {
        var path = http.Request.Path.Value ?? "";
        if (user.Membership is not { } m || http.User.HasClaim(c => c.Type == ArteClaims.OpenMode)
            || AlwaysAllowed.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
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
            var end = await db.Licenses.AsNoTracking().Where(l => l.TenantId == m.TenantId)
                .MaxAsync(l => (DateTimeOffset?)l.EndsAt, http.RequestAborted);
            if (LicensePolicy.StateOf(end, clock.UtcNow).State == LicenseStates.Expired)
            {
                await Results.Problem(statusCode: 402,
                    title: "اعتبار اشتراک این تعمیرگاه تمام شده است. پرونده‌های فعلی را می‌توانید ادامه دهید؛ برای پذیرش جدید اشتراک را تمدید کنید.",
                    extensions: new Dictionary<string, object?> { ["code"] = "license_expired" }).ExecuteAsync(http);
                return;
            }
        }

        await next(http);
    }
}
