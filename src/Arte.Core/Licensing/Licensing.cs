using Arte.Core.Common;

namespace Arte.Core.Licensing;

/// <summary>
/// A plan the platform sells (1, 3, 6, 9, 12 months). Platform-level, not owned by a business.
/// Never deleted: an old plan is switched off so licences sold on it keep pointing at it.
/// </summary>
public sealed class Plan
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public int Months { get; set; }
    public long PriceRials { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class LicenseKinds
{
    public const string Trial = "trial";   // free period at sign-up
    public const string Paid = "paid";     // sold on a plan (payment recorded by the platform admin)
    public const string Gift = "gift";     // free extension granted by the platform admin
    public static readonly IReadOnlySet<string> All = new HashSet<string> { Trial, Paid, Gift };
}

/// <summary>
/// A period during which a branch (today: a tenant) may work normally. Licences of one branch stack:
/// the branch's access runs to the latest <see cref="EndsAt"/>. Managed by the platform admin, so it is not
/// tenant-filtered; every query names its TenantId. A wrong grant is revoked by soft delete.
/// </summary>
public sealed class License : ISoftDeletable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string Kind { get; set; }
    public Guid? PlanId { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    /// <summary>What was paid for it (0 for trial and gifts).</summary>
    public long PriceRials { get; set; }
    public string? Note { get; set; }
    /// <summary>Null when the system granted it (sign-up trial, backfill).</summary>
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}

public static class LicenseStates
{
    public const string Active = "active";
    /// <summary>Active, but ends within <see cref="LicensePolicy.ExpiringDays"/> days.</summary>
    public const string Expiring = "expiring";
    /// <summary>Ended: the branch is read-only (no new intake, customers or staff).</summary>
    public const string Expired = "expired";
}

public static class LicensePolicy
{
    public const int TrialDays = 14;
    public const int ExpiringDays = 3;

    public static (string State, int DaysLeft) StateOf(DateTimeOffset? endsAt, DateTimeOffset now)
    {
        if (endsAt is null || endsAt <= now) return (LicenseStates.Expired, 0);
        var days = (int)Math.Ceiling((endsAt.Value - now).TotalDays);
        return (days <= ExpiringDays ? LicenseStates.Expiring : LicenseStates.Active, days);
    }

    /// <summary>A new licence starts where the current one ends (or now, if it has ended).</summary>
    public static DateTimeOffset NextStart(DateTimeOffset? currentEnd, DateTimeOffset now) =>
        currentEnd is { } end && end > now ? end : now;
}
