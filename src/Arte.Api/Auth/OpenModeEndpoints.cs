using Arte.Api.Options;
using Arte.Api.Tenants;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arte.Api.Auth;

/// <summary>
/// Temporary "no sign-in" mode for setting the system up before real login is switched on.
/// Everyone who opens the site acts as the single owner of one business.
/// Off unless Auth:OpenMode is true; turning it off kills every session issued here.
/// </summary>
public static class OpenModeEndpoints
{
    public const string OwnerMobile = "09000000000";
    public const string BusinessName = "کسب‌وکار من";

    public static void MapOpenMode(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/auth/open", async (ArteDbContext db, TokenService tokens, IServiceScopeFactory scopes,
            IClock clock, IConfiguration config, HttpContext http, IOptions<JwtOptions> jwt, CancellationToken ct) =>
        {
            if (!config.GetValue("Auth:OpenMode", false)) return Results.NotFound();

            var user = await db.Users.SingleOrDefaultAsync(u => u.Mobile == OwnerMobile, ct);
            if (user is null)
            {
                user = new User { Mobile = OwnerMobile, DisplayName = "مدیر", CreatedAt = clock.UtcNow };
                db.Users.Add(user);
                await db.SaveChangesAsync(ct);
            }

            var membership = await FindOwnerMembership(db, user.Id, ct);
            if (membership is null)
            {
                await TenantEndpoints.ProvisionAsync(scopes, user.Id, BusinessName, null, null, ct);
                membership = await FindOwnerMembership(db, user.Id, ct);
            }

            var issued = await tokens.IssueAsync(user.Id, membership, ct, openMode: true);
            AuthEndpoints.SetRefreshCookie(http, issued, jwt.Value);
            return Results.Ok(new
            {
                issued.AccessToken,
                ExpiresAt = issued.AccessExpiresAt,
                membership!.TenantId,
                Memberships = Array.Empty<object>(),
            });
        }).RequireRateLimiting("session");
    }

    private static Task<Membership?> FindOwnerMembership(ArteDbContext db, Guid userId, CancellationToken ct) =>
        db.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UserId == userId && m.Role == Roles.Owner && m.IsActive)
            .OrderBy(m => m.CreatedAt)
            .FirstOrDefaultAsync(ct);
}
