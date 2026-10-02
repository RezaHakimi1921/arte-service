namespace Arte.Core.Tenancy;

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public required string Vertical { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public long LastCaseNumber { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
