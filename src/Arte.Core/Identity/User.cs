using Arte.Core.Tenancy;

namespace Arte.Core.Identity;

/// <summary>A person, identified by mobile. Not tenant-owned: one person can work in several businesses.</summary>
public sealed class User
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Mobile { get; set; }
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Optional second way in, set only from the server console (set-password). OTP stays the default.</summary>
    public string? Username { get; set; }
    public string? PasswordHash { get; set; }
    public int FailedPasswordAttempts { get; set; }
    public DateTimeOffset? PasswordLockedUntil { get; set; }
    /// <summary>Arte staff who run the platform admin panel (businesses, licences, plans). Set only from the server CLI.</summary>
    public bool IsPlatformAdmin { get; set; }
}

public sealed class Membership : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public required string Role { get; set; }
    public string[] Permissions { get; set; } = [];
    public string PayModel { get; set; } = PayModels.None;
    public decimal? CommissionPercent { get; set; }
    public long? FixedMonthlyRials { get; set; }
    /// <summary>none | percent | fixed_per_case (see Billing.CommissionTypes).</summary>
    public string CommissionType { get; set; } = "none";
    public long? CommissionFixedRials { get; set; }
    /// <summary>case_total | labor | labor_plus_parts_profit (see Billing.CommissionBases).</summary>
    public string CommissionBase { get; set; } = "case_total";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>When this member finished or skipped the intro tour; null shows it on next sign-in.</summary>
    public DateTimeOffset? TourDoneAt { get; set; }
    /// <summary>
    /// Gets a bell notification when a customer answers the survey: for every case if they see all cases, otherwise
    /// for their own work. Chosen by the manager per member; on for the owner and supervisors by default.
    /// </summary>
    public bool SurveyNotify { get; set; }

    public bool Has(string permission) => Role == Roles.Owner || Permissions.Contains(permission);
}

public sealed class OtpChallenge
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Mobile { get; set; }
    public required string CodeHash { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public string? RequestIp { get; set; }
    /// <summary>Postgres xmin: two parallel verifies of one code cannot both win.</summary>
    public uint Version { get; set; }
}

/// <summary>Stored hashed. Rotated on every use; reuse of a rotated token revokes the whole family.</summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public Guid FamilyId { get; set; }
    public Guid? TenantId { get; set; }
    public required string TokenHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    /// <summary>Issued while sign-in was switched off; all such sessions die when open mode is turned off.</summary>
    public bool IsOpenMode { get; set; }
    /// <summary>Postgres xmin: a token can be rotated only once.</summary>
    public uint Version { get; set; }
}

/// <summary>Security-relevant actions (logins, staff and permission changes). Append-only.</summary>
public sealed class AuditEvent
{
    public long Id { get; set; }
    public Guid? TenantId { get; set; }
    public Guid? UserId { get; set; }
    public required string Type { get; set; }
    public string? Ip { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}
