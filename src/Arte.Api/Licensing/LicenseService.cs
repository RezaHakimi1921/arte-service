using Arte.Core.Data;
using Arte.Core.Licensing;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Licensing;

public sealed record LicenseStatus(
    string State, int DaysLeft, DateTimeOffset? EndsAt, string? Kind,
    int FreeCasesUsed, int FreeCasesLimit, int FreeCasesLeft, bool OnCaseTrial);

public sealed record LicenseAccess(
    bool CanGrow, bool CanSendSms, string LockCode, string LockMessage,
    int FreeCasesUsed, int FreeCasesLimit, int FreeCasesLeft, bool OnCaseTrial);

/// <summary>Licence rules shared by sign-up, the enforcement middleware, /me and the admin panel.</summary>
public static class LicenseService
{
    public const string TrialCasesExhausted = "trial_cases_exhausted";
    public const string LicenseExpired = "license_expired";

    /// <summary>The live licence that ends last, or null when the branch has none left.</summary>
    public static Task<License?> LatestAsync(ArteDbContext db, Guid tenantId, CancellationToken ct) =>
        db.Licenses.AsNoTracking().Where(l => l.TenantId == tenantId)
            .OrderByDescending(l => l.EndsAt).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Real cases of the branch, including soft-deleted ones (so deleting a case cannot refresh the free quota).
    /// Sample onboarding cases do not count.
    /// </summary>
    public static Task<int> CaseCountAsync(ArteDbContext db, Guid tenantId, CancellationToken ct) =>
        db.Cases.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter, ArteDbContext.TenantFilter])
            .CountAsync(c => c.TenantId == tenantId && !c.IsSample, ct);

    public static async Task<LicenseAccess> AccessAsync(ArteDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var latest = await LatestAsync(db, tenantId, ct);
        var used = await CaseCountAsync(db, tenantId, ct);
        var limit = LicensePolicy.TrialCaseLimit;
        var left = Math.Max(0, limit - used);

        // A live paid/gift licence unlocks everything (no case cap).
        if (latest is { Kind: LicenseKinds.Paid or LicenseKinds.Gift }
            && LicensePolicy.StateOf(latest.EndsAt, now).State != LicenseStates.Expired)
        {
            return new LicenseAccess(true, true, "", "", used, limit, left, OnCaseTrial: false);
        }

        // Case-based trial (new sign-ups): EndsAt is far away; the cap is the case count.
        if (latest is { Kind: LicenseKinds.Trial } && LicensePolicy.StateOf(latest.EndsAt, now).State != LicenseStates.Expired)
        {
            if (used < limit)
                return new LicenseAccess(true, true, "", "", used, limit, left, OnCaseTrial: true);
            return new LicenseAccess(false, false, TrialCasesExhausted,
                "۱۵ پرونده‌ی رایگان این تعمیرگاه تمام شده است. پرونده‌های فعلی را می‌توانید ادامه دهید؛ برای پذیرش و پیامک جدید اشتراک بخرید یا با پشتیبانی آرته تماس بگیرید.",
                used, limit, 0, OnCaseTrial: true);
        }

        return new LicenseAccess(false, false, LicenseExpired,
            "اعتبار اشتراک این تعمیرگاه تمام شده است. پرونده‌های فعلی را می‌توانید ادامه دهید؛ برای پذیرش جدید اشتراک را تمدید کنید.",
            used, limit, left, OnCaseTrial: false);
    }

    public static async Task<LicenseStatus> StatusAsync(ArteDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var latest = await LatestAsync(db, tenantId, ct);
        var access = await AccessAsync(db, tenantId, now, ct);
        var (state, days) = access.CanGrow
            ? LicensePolicy.StateOf(latest?.EndsAt, now)
            : (access.LockCode == TrialCasesExhausted ? LicenseStates.Expired : LicenseStates.Expired, 0);
        if (access is { OnCaseTrial: true, CanGrow: true })
            state = access.FreeCasesLeft <= 3 ? LicenseStates.Expiring : LicenseStates.Active;
        return new LicenseStatus(state, days, latest?.EndsAt, latest?.Kind,
            access.FreeCasesUsed, access.FreeCasesLimit, access.FreeCasesLeft, access.OnCaseTrial);
    }

    /// <summary>New shops: long-lived trial row; growth is gated by <see cref="LicensePolicy.TrialCaseLimit"/> cases.</summary>
    public static License Trial(Guid tenantId, DateTimeOffset now) => new()
    {
        TenantId = tenantId, Kind = LicenseKinds.Trial, StartsAt = now, EndsAt = now.AddYears(50),
        Note = $"آزمایشی: {LicensePolicy.TrialCaseLimit} پرونده رایگان (شامل نظرسنجی)", CreatedAt = now,
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
