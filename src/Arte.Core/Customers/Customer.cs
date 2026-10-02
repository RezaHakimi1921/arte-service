using System.Text.Json;
using Arte.Core.Tenancy;

namespace Arte.Core.Customers;

public sealed class Customer : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string Mobile { get; set; }
    public string? FullName { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<Asset> Assets { get; set; } = [];
}

/// <summary>What the customer brings in: a motorcycle today, any device later.</summary>
public sealed class Asset : ITenantOwned, IDisposable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid CustomerId { get; set; }
    public required string Kind { get; set; }
    public required string Title { get; set; }
    public string? Identifier { get; set; }
    /// <summary>Vertical-specific fields (brand, model, year, color, engine number…).</summary>
    public JsonDocument? Attributes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public void Dispose() => Attributes?.Dispose();
}
