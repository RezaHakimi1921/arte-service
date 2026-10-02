using System.Security.Cryptography;
using System.Text;
using Arte.Api.Options;
using Arte.Api.Security;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Arte.Api.Auth;

public sealed record IssuedTokens(Guid UserId, string AccessToken, DateTimeOffset AccessExpiresAt, string RefreshToken, DateTimeOffset RefreshExpiresAt);

public sealed class TokenService(ArteDbContext db, IOptions<JwtOptions> options, IClock clock, Audit audit)
{
    public const string RefreshCookie = "arte_rt";
    public const string RefreshCookiePath = "/api/v1/auth";

    private readonly JwtOptions _jwt = options.Value;

    public static SymmetricSecurityKey SigningKey(JwtOptions jwt) => new(Convert.FromBase64String(jwt.Key));

    /// <summary>Starts a new refresh family (a fresh login).</summary>
    public Task<IssuedTokens> IssueAsync(Guid userId, Membership? membership, CancellationToken ct) =>
        IssueInFamilyAsync(userId, membership, Guid.CreateVersion7(), ct);

    /// <summary>
    /// Exchanges a refresh token for new tokens. A token is single-use: presenting one that was already
    /// rotated means it was stolen or replayed, so the whole family is revoked.
    /// </summary>
    public async Task<IssuedTokens?> RotateAsync(string presented, Func<Guid, Guid?, Task<Membership?>> resolveMembership,
        CancellationToken ct)
    {
        var hash = Hash(presented);
        var token = await db.RefreshTokens.SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) return null;

        var now = clock.UtcNow;
        if (token.RevokedAt is not null)
        {
            if (token.RevokedReason == "rotated")
            {
                await RevokeFamilyAsync(token.FamilyId, "reuse_detected", ct);
                audit.Record("auth.refresh_reuse", token.TenantId, token.UserId);
                await db.SaveChangesAsync(ct);
            }
            return null;
        }
        if (token.ExpiresAt <= now) return null;

        token.RevokedAt = now;
        token.RevokedReason = "rotated";
        var membership = await resolveMembership(token.UserId, token.TenantId);

        try
        {
            return await IssueInFamilyAsync(token.UserId, membership, token.FamilyId, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Two requests raced with the same token; only one may win.
            return null;
        }
    }

    public async Task RevokeFamilyByTokenAsync(string presented, CancellationToken ct)
    {
        var hash = Hash(presented);
        var token = await db.RefreshTokens.AsNoTracking().SingleOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is null) return;
        await RevokeFamilyAsync(token.FamilyId, "logout", ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Signs a user out of one business everywhere (e.g. when the owner deactivates them).</summary>
    public Task RevokeForTenantAsync(Guid userId, Guid tenantId, CancellationToken ct) =>
        db.RefreshTokens
            .Where(t => t.UserId == userId && t.TenantId == tenantId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, clock.UtcNow)
                                      .SetProperty(t => t.RevokedReason, "membership_changed"), ct);

    private async Task RevokeFamilyAsync(Guid familyId, string reason, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var live = await db.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in live)
        {
            t.RevokedAt = now;
            t.RevokedReason = reason;
        }
    }

    private async Task<IssuedTokens> IssueInFamilyAsync(Guid userId, Membership? membership, Guid familyId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var refresh = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var refreshExpires = now.AddDays(_jwt.RefreshTokenDays);
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            FamilyId = familyId,
            TenantId = membership?.TenantId,
            TokenHash = Hash(refresh),
            CreatedAt = now,
            ExpiresAt = refreshExpires,
        });
        await db.SaveChangesAsync(ct);

        var accessExpires = now.AddMinutes(_jwt.AccessTokenMinutes);
        var claims = new Dictionary<string, object>
        {
            [ArteClaims.UserId] = userId.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
        };
        if (membership is not null)
        {
            claims[ArteClaims.TenantId] = membership.TenantId.ToString();
            claims[ArteClaims.MembershipId] = membership.Id.ToString();
        }

        var access = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            Claims = claims,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = accessExpires.UtcDateTime,
            SigningCredentials = new SigningCredentials(SigningKey(_jwt), SecurityAlgorithms.HmacSha256),
        });

        return new IssuedTokens(userId, access, accessExpires, refresh, refreshExpires);
    }

    public static string Hash(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
