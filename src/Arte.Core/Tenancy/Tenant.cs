namespace Arte.Core.Tenancy;

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string Vertical { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public long LastCaseNumber { get; set; }
    /// <summary>Business setting: every new case must be assigned to someone at intake.</summary>
    public bool RequireAssigneeOnIntake { get; set; } = true;
    /// <summary>Business setting: a «منتظر تأیید مشتری» step after diagnosis. Off by default: the customer already asked for the work.</summary>
    public bool RequireCustomerApproval { get; set; }
    /// <summary>Business setting: the master checks finished work before the customer is told it is ready.</summary>
    public bool RequireFinalReview { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Platform admin switch: an inactive branch cannot be used at all (data is kept).</summary>
    public bool IsActive { get; set; } = true;
    public string? DeactivatedReason { get; set; }
}
