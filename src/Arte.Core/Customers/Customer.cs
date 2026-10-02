using System.Text.Json;
using Arte.Core.Common;
using Arte.Core.Tenancy;

namespace Arte.Core.Customers;

public sealed class Customer : ITenantOwned, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string Mobile { get; set; }
    public string? FullName { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public List<Asset> Assets { get; set; } = [];
}

public static class AssetKinds
{
    public const string Vehicle = "vehicle";
    public const string Car = "car";
    public const string Motorcycle = "motorcycle";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Vehicle, Car, Motorcycle };
}

/// <summary>What the customer brings in: a vehicle today, any device later.</summary>
public sealed class Asset : ITenantOwned, ISoftDeletable, IDisposable
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
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public void Dispose() => Attributes?.Dispose();
}
