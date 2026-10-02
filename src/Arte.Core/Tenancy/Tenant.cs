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
    public DateTimeOffset CreatedAt { get; set; }
}
