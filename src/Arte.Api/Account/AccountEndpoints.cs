using Arte.Api.Security;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Account;

public static class AccountEndpoints
{
    public sealed record UpdateAccount(string? DisplayName, string? Username, string? NewPassword, string? CurrentPassword);

    public static void MapAccount(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/account").RequireAuthorization();

        g.MapGet("/", async (RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == me.RequiredUserId, ct);
            return Results.Ok(new { u.DisplayName, u.Username, u.Mobile, HasPassword = u.PasswordHash != null });
        });

        g.MapPut("/", async (UpdateAccount req, RequestUser me, ArteDbContext db, Audit audit, IClock clock, CancellationToken ct) =>
        {
            var user = await db.Users.SingleAsync(x => x.Id == me.RequiredUserId, ct);
            var errors = new Dictionary<string, string[]>();

            var name = req.DisplayName?.Trim();
            if (name is { Length: > 80 }) errors["displayName"] = ["نام حداکثر ۸۰ حرف."];

            var username = req.Username?.Trim().ToLowerInvariant();
            if (username is not null && (username.Length is < 3 or > 40
                || !username.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_' or '-')))
                errors["username"] = ["نام کاربری ۳ تا ۴۰ حرف انگلیسی، عدد، نقطه، خط تیره یا زیرخط."];

            var changingCredentials = req.NewPassword is not null || (username is not null && username != user.Username);
            if (req.NewPassword is { } pw)
            {
                if (pw.Length < PasswordHasher.MinLength || pw.Length > 200)
                    errors["newPassword"] = [$"رمز باید حداقل {PasswordHasher.MinLength} کاراکتر باشد."];
                else if (string.Equals(pw, username ?? user.Username, StringComparison.OrdinalIgnoreCase) || pw == user.Mobile)
                    errors["newPassword"] = ["رمز نباید با شماره موبایل یا نام کاربری یکی باشد."];
            }
            // The mobile number is the sign-in name; a username is optional (kept for older accounts).
            if (changingCredentials && user.PasswordHash is null && req.NewPassword is null) errors["newPassword"] = ["رمز لازم است."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            // Once a password exists, changing credentials needs it: an open session alone is not enough.
            if (changingCredentials && user.PasswordHash is not null)
            {
                if (user.PasswordLockedUntil > clock.UtcNow)
                    return Results.Problem(statusCode: 429, title: "به‌خاطر تلاش‌های ناموفق، موقتاً قفل است.");
                if (string.IsNullOrEmpty(req.CurrentPassword) || req.CurrentPassword.Length > 200
                    || !PasswordHasher.Verify(req.CurrentPassword, user.PasswordHash))
                {
                    if (++user.FailedPasswordAttempts >= 5)
                    {
                        user.PasswordLockedUntil = clock.UtcNow.AddMinutes(15);
                        user.FailedPasswordAttempts = 0;
                    }
                    await db.SaveChangesAsync(ct);
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["currentPassword"] = ["رمز فعلی نادرست است."] });
                }
            }

            if (username is not null && username != user.Username)
            {
                if (await db.Users.AnyAsync(x => x.Username == username && x.Id != user.Id, ct))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["username"] = ["این نام کاربری گرفته شده است."] });
                user.Username = username;
            }
            if (req.NewPassword is not null)
            {
                user.PasswordHash = PasswordHasher.Hash(req.NewPassword);
                user.FailedPasswordAttempts = 0;
                user.PasswordLockedUntil = null;
            }
            if (req.DisplayName is not null) user.DisplayName = string.IsNullOrEmpty(name) ? null : name;

            if (changingCredentials) audit.Record("account.credentials_changed", null, user.Id, user.Username);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { user.DisplayName, user.Username, user.Mobile, HasPassword = user.PasswordHash != null });
        }).RequireRateLimiting("auth");
    }
}
