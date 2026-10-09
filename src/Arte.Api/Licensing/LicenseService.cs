using Arte.Core.Data;
using Arte.Core.Licensing;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Licensing;

public sealed record LicenseStatus(string State, int DaysLeft, DateTimeOffset? EndsAt, string? Kind);

/// <summary>Licence rules shared by sign-up, the enforcement middleware, /me and the admin panel.</summary>
public static class LicenseService
{
    /// <summary>The live licence that ends last, or null when the branch has none left.</summary>
    public static Task<License?> LatestAsync(ArteDbContext db, Guid tenantId, CancellationToken ct) =>
        db.Licenses.AsNoTracking().Where(l => l.TenantId == tenantId)
            .OrderByDescending(l => l.EndsAt).FirstOrDefaultAsync(ct);

    public static async Task<LicenseStatus> StatusAsync(ArteDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var latest = await LatestAsync(db, tenantId, ct);
        var (state, days) = LicensePolicy.StateOf(latest?.EndsAt, now);
        return new LicenseStatus(state, days, latest?.EndsAt, latest?.Kind);
    }

    public static License Trial(Guid tenantId, DateTimeOffset now) => new()
    {
        TenantId = tenantId, Kind = LicenseKinds.Trial, StartsAt = now, EndsAt = now.AddDays(LicensePolicy.TrialDays),
        Note = "دوره‌ی رایگان ثبت‌نام", CreatedAt = now,
    };

    /// <summary>
    /// Businesses created before licences existed get one trial from today, once: a business that ever had a
    /// licence (even a revoked one) is left alone, so a revoke is not undone by a restart.
    /// </summary>
    public static async Task<int> BackfillAsync(ArteDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var licensed = db.Licenses.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).Select(l => l.TenantId);
        var missing = await db.Tenants.Where(t => !licensed.Contains(t.Id)).Select(t => t.Id).ToListAsync(ct);
        foreach (var id in missing) db.Licenses.Add(Trial(id, now));
        if (missing.Count > 0) await db.SaveChangesAsync(ct);
        return missing.Count;
    }

    /// <summary>The owner's price list (toman → rials), created once; afterwards prices are edited in the admin panel.</summary>
    public static async Task SeedPlansAsync(ArteDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.Plans.AnyAsync(ct)) return;
        (string Name, int Months, long Toman)[] plans =
        [
            ("یک ماهه", 1, 1_000_000), ("سه ماهه", 3, 2_500_000), ("شش ماهه", 6, 5_000_000),
            ("نه ماهه", 9, 7_500_000), ("یک ساله", 12, 10_000_000),
        ];
        var order = 0;
        foreach (var p in plans)
            db.Plans.Add(new Plan { Name = p.Name, Months = p.Months, PriceRials = p.Toman * 10, SortOrder = order++, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync(ct);
    }
}
