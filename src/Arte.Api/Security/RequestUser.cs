using System.Security.Claims;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Security;

public static class ArteClaims
{
    public const string UserId = "sub";
    public const string TenantId = "tid";
    public const string MembershipId = "mid";
    /// <summary>Present on tokens issued while sign-in was switched off.</summary>
    public const string OpenMode = "om";
}

/// <summary>Who is calling, and as which member of which business. Filled once per request.</summary>
public sealed class RequestUser
{
    public Guid? UserId { get; set; }
    public Membership? Membership { get; set; }

    public Guid RequiredUserId => UserId ?? throw new InvalidOperationException("No authenticated user.");
    public Membership RequiredMembership => Membership ?? throw new InvalidOperationException("No business selected.");
}

/// <summary>
/// Turns the access token into a live membership. The membership is re-read on every request,
/// so deactivating a staff member or changing their permissions takes effect immediately,
/// not when their 15-minute token expires.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http, RequestUser user, TenantContext tenant, ArteDbContext db, IConfiguration config)
    {
        if (http.User.Identity?.IsAuthenticated == true)
        {
            if (http.User.HasClaim(c => c.Type == ArteClaims.OpenMode) && !config.GetValue("Auth:OpenMode", false))
            {
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            if (!Guid.TryParse(http.User.FindFirstValue(ArteClaims.UserId), out var userId))
            {
                http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
            user.UserId = userId;

            var tid = http.User.FindFirstValue(ArteClaims.TenantId);
            var mid = http.User.FindFirstValue(ArteClaims.MembershipId);
            if (tid is not null || mid is not null)
            {
                if (!Guid.TryParse(tid, out var tenantId) || !Guid.TryParse(mid, out var membershipId))
                {
                    http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                var membership = await db.Memberships.IgnoreQueryFilters().AsNoTracking()
                    .SingleOrDefaultAsync(m => m.Id == membershipId && m.UserId == userId
                                               && m.TenantId == tenantId && m.IsActive, http.RequestAborted);
                if (membership is null)
                {
                    http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                user.Membership = membership;
                tenant.Set(tenantId);
            }
        }

        await next(http);
    }
}

public static class PermissionEndpointExtensions
{
    /// <summary>Caller must have selected a business.</summary>
    public static TBuilder RequireTenant<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization().AddEndpointFilter(async (ctx, next) =>
        {
            var user = ctx.HttpContext.RequestServices.GetRequiredService<RequestUser>();
            return user.Membership is null
                ? Results.Problem(statusCode: 403, title: "ابتدا کسب‌وکار را انتخاب کنید.")
                : await next(ctx);
        });

    /// <summary>Caller must have selected a business and hold every listed permission.</summary>
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, params string[] permissions)
        where TBuilder : IEndpointConventionBuilder =>
        builder.RequireAuthorization().AddEndpointFilter(async (ctx, next) =>
        {
            var membership = ctx.HttpContext.RequestServices.GetRequiredService<RequestUser>().Membership;
            if (membership is null)
                return Results.Problem(statusCode: 403, title: "ابتدا کسب‌وکار را انتخاب کنید.");
            if (!permissions.All(membership.Has))
                return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");
            return await next(ctx);
        });
}
