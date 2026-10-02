using Arte.Api.Options;
using Arte.Api.Security;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arte.Api.Auth;

public static class AuthEndpoints
{
    public sealed record OtpRequest(string? Mobile);
    public sealed record OtpVerify(string? Mobile, string? Code);
    public sealed record SelectTenant(Guid TenantId);
    public sealed record MembershipView(Guid TenantId, string TenantName, string Role);
    public sealed record SessionView(string AccessToken, DateTimeOffset ExpiresAt, Guid? TenantId, IReadOnlyList<MembershipView> Memberships);

    public static void MapAuth(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/auth").RequireRateLimiting("auth");

        g.MapPost("/otp/request", async (OtpRequest req, OtpService otp, HttpContext http, CancellationToken ct) =>
        {
            if (!Mobile.TryNormalize(req.Mobile, out var mobile))
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["mobile"] = ["شماره موبایل معتبر نیست."] });

            return await otp.RequestAsync(mobile, http.Connection.RemoteIpAddress?.ToString(), ct) switch
            {
                OtpRequestResult.Sent => Results.Accepted(value: new { expiresInSeconds = 120 }),
                OtpRequestResult.TooSoon => Results.Problem(statusCode: 429, title: "کمی صبر کنید و دوباره درخواست دهید."),
                _ => Results.Problem(statusCode: 429, title: "تعداد درخواست‌ها زیاد است. بعداً تلاش کنید."),
            };
        });

        g.MapPost("/otp/verify", async (OtpVerify req, OtpService otp, ArteDbContext db, TokenService tokens,
            Audit audit, IClock clock, HttpContext http, IOptions<JwtOptions> jwt, CancellationToken ct) =>
        {
            if (!Mobile.TryNormalize(req.Mobile, out var mobile) || string.IsNullOrEmpty(req.Code)
                || !await otp.VerifyAsync(mobile, req.Code.Trim(), ct))
                return Results.Problem(statusCode: 400, title: "کد نادرست است یا منقضی شده.");

            var user = await db.Users.SingleOrDefaultAsync(u => u.Mobile == mobile, ct);
            if (user is null)
            {
                user = new User { Mobile = mobile, CreatedAt = clock.UtcNow };
                db.Users.Add(user);
            }
            user.LastLoginAt = clock.UtcNow;
            audit.Record("auth.login", null, user.Id);
            await db.SaveChangesAsync(ct);

            var memberships = await ActiveMemberships(db, user.Id, ct);
            var selected = memberships.Count == 1 ? memberships[0].Membership : null;
            var issued = await tokens.IssueAsync(user.Id, selected, ct);
            SetRefreshCookie(http, issued, jwt.Value);
            return Results.Ok(ToSession(issued, selected, memberships));
        });

        g.MapPost("/refresh", async (ArteDbContext db, TokenService tokens, HttpContext http,
            IOptions<JwtOptions> jwt, CancellationToken ct) =>
        {
            if (!http.Request.Cookies.TryGetValue(TokenService.RefreshCookie, out var presented) || presented.Length > 100)
                return Results.Unauthorized();

            Membership? selected = null;
            var issued = await tokens.RotateAsync(presented, async (userId, tenantId) =>
            {
                if (tenantId is null) return null;
                selected = await db.Memberships.IgnoreQueryFilters().AsNoTracking()
                    .SingleOrDefaultAsync(m => m.UserId == userId && m.TenantId == tenantId && m.IsActive, ct);
                return selected;
            }, ct);

            if (issued is null)
            {
                ClearRefreshCookie(http);
                return Results.Unauthorized();
            }

            SetRefreshCookie(http, issued, jwt.Value);
            return Results.Ok(ToSession(issued, selected, await ActiveMemberships(db, issued.UserId, ct)));
        });

        g.MapPost("/select-tenant", async (SelectTenant req, RequestUser me, ArteDbContext db, TokenService tokens,
            HttpContext http, IOptions<JwtOptions> jwt, CancellationToken ct) =>
        {
            var membership = await db.Memberships.IgnoreQueryFilters().AsNoTracking()
                .SingleOrDefaultAsync(m => m.UserId == me.RequiredUserId && m.TenantId == req.TenantId && m.IsActive, ct);
            if (membership is null) return Results.Problem(statusCode: 403, title: "عضو این کسب‌وکار نیستید.");

            if (http.Request.Cookies.TryGetValue(TokenService.RefreshCookie, out var old))
                await tokens.RevokeFamilyByTokenAsync(old, ct);

            var issued = await tokens.IssueAsync(me.RequiredUserId, membership, ct);
            SetRefreshCookie(http, issued, jwt.Value);
            return Results.Ok(ToSession(issued, membership, await ActiveMemberships(db, me.RequiredUserId, ct)));
        }).RequireAuthorization();

        g.MapPost("/logout", async (TokenService tokens, HttpContext http, CancellationToken ct) =>
        {
            if (http.Request.Cookies.TryGetValue(TokenService.RefreshCookie, out var presented) && presented.Length <= 100)
                await tokens.RevokeFamilyByTokenAsync(presented, ct);
            ClearRefreshCookie(http);
            return Results.NoContent();
        });
    }

    private sealed record ActiveMembership(Membership Membership, string TenantName);

    private static async Task<List<ActiveMembership>> ActiveMemberships(ArteDbContext db, Guid userId, CancellationToken ct) =>
        await db.Memberships.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.UserId == userId && m.IsActive)
            .Join(db.Tenants, m => m.TenantId, t => t.Id, (m, t) => new ActiveMembership(m, t.Name))
            .ToListAsync(ct);

    private static SessionView ToSession(IssuedTokens issued, Membership? selected, List<ActiveMembership> all) =>
        new(issued.AccessToken, issued.AccessExpiresAt, selected?.TenantId,
            all.Select(a => new MembershipView(a.Membership.TenantId, a.TenantName, a.Membership.Role)).ToList());

    private static void SetRefreshCookie(HttpContext http, IssuedTokens issued, JwtOptions jwt) =>
        http.Response.Cookies.Append(TokenService.RefreshCookie, issued.RefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = TokenService.RefreshCookiePath,
            Expires = issued.RefreshExpiresAt,
            IsEssential = true,
        });

    private static void ClearRefreshCookie(HttpContext http) =>
        http.Response.Cookies.Delete(TokenService.RefreshCookie, new CookieOptions
        {
            HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict, Path = TokenService.RefreshCookiePath,
        });
}
