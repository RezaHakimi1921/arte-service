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
    public sealed record AddStaff(string? Mobile, string? DisplayName, string? Role);

    public sealed record UpdateStaff(
        string? Role, string[]? Permissions, bool? IsActive,
        string? PayModel, decimal? CommissionPercent, long? FixedMonthlyRials,
        string? DisplayName, string? CommissionType, long? CommissionFixedRials, string? CommissionBase);

    public static void MapStaff(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/staff").RequirePermission(Permissions.StaffManage);

        g.MapGet("/", async (ArteDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Memberships.AsNoTracking()
                .OrderBy(m => m.CreatedAt)
                .Select(m => new
                {
                    m.Id, m.UserId, m.User!.Mobile, m.User.DisplayName, m.Role,
                    Permissions = m.Role == Roles.Owner ? Permissions.All.ToArray() : m.Permissions,
                    m.IsActive, m.PayModel, m.CommissionPercent, m.FixedMonthlyRials,
                    m.CommissionType, m.CommissionFixedRials, m.CommissionBase,
                })
                .ToListAsync(ct)));

        g.MapPost("/", async (AddStaff req, RequestUser me, ArteDbContext db, Audit audit, IClock clock, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (!Mobile.TryNormalize(req.Mobile, out var mobile)) errors["mobile"] = ["شماره موبایل معتبر نیست."];
            var role = req.Role ?? Roles.Technician;
            if (role is not (Roles.Supervisor or Roles.Technician)) errors["role"] = ["نقش باید supervisor یا technician باشد."];
            var name = req.DisplayName?.Trim();
            if (name is { Length: > 80 }) errors["displayName"] = ["نام حداکثر ۸۰ حرف."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var granted = Roles.DefaultPermissions(role);
            if (!CanGrant(me.RequiredMembership, granted))
                return Results.Problem(statusCode: 403, title: "نمی‌توانید دسترسی‌ای بیشتر از دسترسی خودتان بدهید.");

            var user = await db.Users.SingleOrDefaultAsync(u => u.Mobile == mobile, ct);
            if (user is null)
            {
                user = new User { Mobile = mobile, DisplayName = string.IsNullOrEmpty(name) ? null : name, CreatedAt = clock.UtcNow };
                db.Users.Add(user);
            }
            else if (await db.Memberships.AnyAsync(m => m.UserId == user.Id, ct))
            {
                return Results.Problem(statusCode: 409, title: "این شماره قبلاً در کارکنان ثبت شده است.");
            }

            var membership = new Membership
            {
                UserId = user.Id, Role = role, Permissions = granted, CreatedAt = clock.UtcNow,
            };
            db.Memberships.Add(membership);
            audit.Record("staff.added", me.RequiredMembership.TenantId, me.RequiredUserId, $"{membership.Id} {role}");
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
            if (req.Role is not null and not (Roles.Supervisor or Roles.Technician)) errors["role"] = ["نقش نامعتبر است."];
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
            if (displayName is not null && target.User is not null) target.User.DisplayName = displayName.Length == 0 ? null : displayName;

            audit.Record("staff.updated", target.TenantId, me.RequiredUserId,
                $"{target.Id} role={target.Role} active={target.IsActive} perms={string.Join(',', target.Permissions)}");
            await db.SaveChangesAsync(ct);

            if (securityChanged && !target.IsActive)
                await tokens.RevokeForTenantAsync(target.UserId, target.TenantId, ct);

            return Results.NoContent();
        });
    }

    /// <summary>Nobody can hand out a permission they do not hold themselves.</summary>
    private static bool CanGrant(Membership granter, IEnumerable<string> permissions) =>
        permissions.All(granter.Has);
}
