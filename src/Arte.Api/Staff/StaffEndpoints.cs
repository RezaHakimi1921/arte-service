using Arte.Api.Auth;
using Arte.Api.Security;
using Arte.Core.Billing;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Staff;

public static class StaffEndpoints
{
    /// <summary>Username and password are optional: a person always signs in with an SMS code; with a password they can sign in with it too.</summary>
    public sealed record AddStaff(string? Mobile, string? DisplayName, string? Role, string? Username = null, string? Password = null);

    /// <summary>The manager sets (or resets) another member's sign-in name and/or password.</summary>
    public sealed record SetCredentials(string? Username, string? NewPassword);

    public sealed record UpdateStaff(
        string? Role, string[]? Permissions, bool? IsActive,
        string? PayModel, decimal? CommissionPercent, long? FixedMonthlyRials,
        string? DisplayName, string? CommissionType, long? CommissionFixedRials, string? CommissionBase, bool? SurveyNotify = null);

    public static void MapStaff(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/staff").RequirePermission(Permissions.StaffManage);

        g.MapGet("/", async (ArteDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Memberships.AsNoTracking()
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    m.Id, m.UserId, m.User!.Mobile, m.User!.Username, HasPassword = m.User!.PasswordHash != null, m.DisplayName, m.Role,
                    Permissions = m.Role == Roles.Owner ? Permissions.All.ToArray() : m.Permissions,
                    m.IsActive, m.PayModel, m.CommissionPercent, m.FixedMonthlyRials,
                    m.CommissionType, m.CommissionFixedRials, m.CommissionBase, m.SurveyNotify,
                })
                .ToListAsync(ct)));

        MapCredentials(g);

        g.MapPost("/", async (AddStaff req, RequestUser me, ArteDbContext db, Audit audit, IClock clock, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (!Mobile.TryNormalize(req.Mobile, out var mobile)) errors["mobile"] = ["شماره موبایل معتبر نیست."];
            var role = req.Role ?? Roles.Technician;
            if (role is not (Roles.Supervisor or Roles.Technician or Roles.Apprentice)) errors["role"] = ["نقش نامعتبر است."];
            var name = req.DisplayName?.Trim();
            if (name is { Length: > 80 }) errors["displayName"] = ["نام حداکثر ۸۰ حرف."];
            var username = CredentialRules.NormalizeUsername(req.Username);
            var password = string.IsNullOrEmpty(req.Password) ? null : req.Password;
            if (errors.ContainsKey("mobile") is false) CredentialRules.Validate(errors, username, password, mobile);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var granted = Roles.DefaultPermissions(role);
            if (!CanGrant(me.RequiredMembership, granted))
                return Results.Problem(statusCode: 403, title: "نمی‌توانید دسترسی‌ای بیشتر از دسترسی خودتان بدهید.");

            var user = await db.Users.SingleOrDefaultAsync(u => u.Mobile == mobile, ct);
            if (user is null)
            {
                if (username is not null && await db.Users.AnyAsync(u => u.Username == username, ct))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["username"] = ["این نام کاربری گرفته شده است."] });
                user = new User
                {
                    Mobile = mobile, DisplayName = string.IsNullOrEmpty(name) ? null : name, CreatedAt = clock.UtcNow,
                    Username = username, PasswordHash = password is null ? null : PasswordHasher.Hash(password),
                };
                db.Users.Add(user);
            }
            else
            {
                // A person who already has an account (maybe in another branch) keeps their own password: nobody else sets it here.
                if (username is not null || password is not null)
                    return Results.ValidationProblem(new Dictionary<string, string[]>
                        { ["password"] = ["این شماره قبلاً حساب دارد؛ رمز و نام کاربری را خود شخص در «حساب من» تعیین می‌کند."] });
                var existing = await db.Memberships.IgnoreQueryFilters([ArteDbContext.SupportFilter]).SingleOrDefaultAsync(m => m.UserId == user.Id, ct);
                if (existing is { IsSupport: true })
                {
                    // Arte staff who were only visiting this branch (support access) can be added as real members:
                    // the hidden support membership becomes a normal one with the chosen role.
                    existing.IsSupport = false;
                    existing.IsActive = true;
                    existing.Role = role;
                    existing.Permissions = granted;
                    existing.DisplayName = string.IsNullOrEmpty(name) ? null : name;
                    existing.SurveyNotify = role == Roles.Supervisor;
                    audit.Record("staff.added", me.RequiredMembership.TenantId, me.RequiredUserId, $"{existing.Id} {role} (from support access)");
                    await db.SaveChangesAsync(ct);
                    return Results.Created($"/api/v1/staff/{existing.Id}", new { existing.Id });
                }
                if (existing is not null)
                    return Results.Problem(statusCode: 409, title: "این شماره قبلاً در کارکنان ثبت شده است.");
            }

            var membership = new Membership
            {
                UserId = user.Id, Role = role, Permissions = granted, CreatedAt = clock.UtcNow, TourDoneAt = clock.UtcNow,
                // The name is this branch's own: an existing user's name from another branch is never read or reused here.
                DisplayName = string.IsNullOrEmpty(name) ? null : name,
                // Supervisors follow customer feedback by default; the manager can switch it per member.
                SurveyNotify = role == Roles.Supervisor,
            };
            db.Memberships.Add(membership);
            audit.Record("staff.added", me.RequiredMembership.TenantId, me.RequiredUserId, $"{membership.Id} {role}{(password is null ? "" : " with password")}");
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/staff/{membership.Id}", new { membership.Id });
        });

        g.MapPatch("/{id:guid}", async (Guid id, UpdateStaff req, RequestUser me, ArteDbContext db, Audit audit,
            TokenService tokens, CancellationToken ct) =>
        {
            var target = await db.Memberships.Include(m => m.User).SingleOrDefaultAsync(m => m.Id == id, ct);
            if (target is null) return Results.NotFound();
            var touchesAccess = req.Role is not null || req.Permissions is not null || req.IsActive is not null;
            if (touchesAccess && target.Id == me.RequiredMembership.Id)
                return Results.Problem(statusCode: 403, title: "دسترسی خودتان را نمی‌توانید تغییر دهید.");
            if (touchesAccess && target.Role == Roles.Owner)
                return Results.Problem(statusCode: 403, title: "دسترسی مالک قابل تغییر نیست.");
            if (target.Id != me.RequiredMembership.Id && target.Role == Roles.Owner && me.RequiredMembership.Role != Roles.Owner)
                return Results.Problem(statusCode: 403, title: "اطلاعات مالک قابل تغییر نیست.");
            if (target.Id != me.RequiredMembership.Id && !CanGrant(me.RequiredMembership, target.Permissions))
                return Results.Problem(statusCode: 403, title: "این عضو دسترسی‌ای بیشتر از شما دارد.");

            var errors = new Dictionary<string, string[]>();
            if (req.Role is not null and not (Roles.Supervisor or Roles.Technician or Roles.Apprentice)) errors["role"] = ["نقش نامعتبر است."];
            if (req.Permissions is { } perms && (perms.Length > 20 || perms.Any(p => !Permissions.All.Contains(p))))
                errors["permissions"] = ["دسترسی نامعتبر است."];
            if (req.PayModel is not null && !PayModels.All.Contains(req.PayModel)) errors["payModel"] = ["مدل دستمزد نامعتبر است."];
            if (req.CommissionPercent is < 0 or > 100) errors["commissionPercent"] = ["درصد باید بین ۰ و ۱۰۰ باشد."];
            if (req.FixedMonthlyRials is < 0 or > 100_000_000_000) errors["fixedMonthlyRials"] = ["مبلغ نامعتبر است."];
            if (req.CommissionType is not null && !CommissionTypes.All.Contains(req.CommissionType)) errors["commissionType"] = ["نوع پورسانت نامعتبر است."];
            if (req.CommissionBase is not null && !CommissionBases.All.Contains(req.CommissionBase)) errors["commissionBase"] = ["مبنای پورسانت نامعتبر است."];
            if (req.CommissionFixedRials is < 0 or > 100_000_000_000) errors["commissionFixedRials"] = ["مبلغ نامعتبر است."];
            var displayName = req.DisplayName?.Trim();
            if (displayName is { Length: > 80 }) errors["displayName"] = ["نام حداکثر ۸۰ حرف."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var newPermissions = req.Permissions?.Distinct().ToArray()
                                 ?? (req.Role is not null ? Roles.DefaultPermissions(req.Role) : null);
            if (newPermissions is not null && !CanGrant(me.RequiredMembership, newPermissions))
                return Results.Problem(statusCode: 403, title: "نمی‌توانید دسترسی‌ای بیشتر از دسترسی خودتان بدهید.");

            var securityChanged = false;
            if (req.Role is not null) target.Role = req.Role;
            if (newPermissions is not null)
            {
                securityChanged |= !newPermissions.ToHashSet().SetEquals(target.Permissions);
                target.Permissions = newPermissions;
            }
            if (req.IsActive is { } active && active != target.IsActive)
            {
                target.IsActive = active;
                securityChanged = true;
            }
            if (req.PayModel is not null) target.PayModel = req.PayModel;
            if (req.CommissionPercent is not null) target.CommissionPercent = req.CommissionPercent;
            if (req.FixedMonthlyRials is not null) target.FixedMonthlyRials = req.FixedMonthlyRials == 0 ? null : req.FixedMonthlyRials;
            if (req.CommissionType is not null) target.CommissionType = req.CommissionType;
            if (req.CommissionFixedRials is not null) target.CommissionFixedRials = req.CommissionFixedRials == 0 ? null : req.CommissionFixedRials;
            if (req.CommissionBase is not null) target.CommissionBase = req.CommissionBase;
            if (req.SurveyNotify is { } notify) target.SurveyNotify = notify;
            if (displayName is not null) target.DisplayName = displayName.Length == 0 ? null : displayName;

            audit.Record("staff.updated", target.TenantId, me.RequiredUserId,
                $"{target.Id} role={target.Role} active={target.IsActive} perms={string.Join(',', target.Permissions)}");
            await db.SaveChangesAsync(ct);

            if (securityChanged && !target.IsActive)
                await tokens.RevokeForTenantAsync(target.UserId, target.TenantId, ct);

            return Results.NoContent();
        });
    }

    private static void MapCredentials(RouteGroupBuilder g)
    {
        g.MapPut("/{id:guid}/credentials", async (Guid id, SetCredentials req, RequestUser me, ArteDbContext db, Audit audit,
            TokenService tokens, CancellationToken ct) =>
        {
            var target = await db.Memberships.Include(m => m.User).SingleOrDefaultAsync(m => m.Id == id, ct);
            if (target?.User is not { } user) return Results.NotFound();
            if (target.Id == me.RequiredMembership.Id)
                return Results.Problem(statusCode: 400, title: "رمز خودتان را از «حساب من» در تنظیمات تغییر دهید.");
            if (target.Role == Roles.Owner && me.RequiredMembership.Role != Roles.Owner)
                return Results.Problem(statusCode: 403, title: "رمز مالک را فقط خودش می‌تواند تغییر دهد.");
            if (!CanGrant(me.RequiredMembership, target.Permissions))
                return Results.Problem(statusCode: 403, title: "این عضو دسترسی‌ای بیشتر از شما دارد.");

            // One account can belong to several branches: a manager of one branch must not take it over.
            var elsewhere = await db.Memberships.IgnoreQueryFilters([ArteDbContext.TenantFilter, ArteDbContext.SupportFilter])
                .AnyAsync(m => m.UserId == user.Id && m.TenantId != target.TenantId && !m.IsSupport, ct);
            if (elsewhere)
                return Results.Problem(statusCode: 409, title: "این شخص در شعبه‌ی دیگری هم عضو است؛ رمز را خودش در «حساب من» تعیین می‌کند.");

            var username = CredentialRules.NormalizeUsername(req.Username);
            var password = string.IsNullOrEmpty(req.NewPassword) ? null : req.NewPassword;
            if (username is null && password is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["newPassword"] = ["رمز جدید یا نام کاربری را وارد کنید."] });
            var errors = new Dictionary<string, string[]>();
            CredentialRules.Validate(errors, username, password, user.Mobile);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            if (username is not null && username != user.Username)
            {
                if (await db.Users.AnyAsync(u => u.Username == username && u.Id != user.Id, ct))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["username"] = ["این نام کاربری گرفته شده است."] });
                user.Username = username;
            }
            if (password is not null)
            {
                user.PasswordHash = PasswordHasher.Hash(password);
                user.FailedPasswordAttempts = 0;
                user.PasswordLockedUntil = null;
            }

            // Never log the password itself.
            audit.Record("staff.credentials_set", target.TenantId, me.RequiredUserId,
                $"{target.Id}{(username is null ? "" : " username")}{(password is null ? "" : " password")}");
            await db.SaveChangesAsync(ct);
            // The old password may be what leaked: every session of that person ends.
            if (password is not null) await tokens.RevokeAllForUserAsync(user.Id, "credentials_reset_by_manager", ct);
            return Results.NoContent();
        }).RequireRateLimiting("auth");
    }

    /// <summary>Nobody can hand out a permission they do not hold themselves.</summary>
    private static bool CanGrant(Membership granter, IEnumerable<string> permissions) =>
        permissions.All(granter.Has);
}
