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
    /// <summary>Created for the first-run tour; removable in one go, never counted for licences or reports.</summary>
    public bool IsSample { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public List<Asset> Assets { get; set; } = [];
}

public static class AssetKinds
{
    public const string Car = "car";            // سواری
    public const string Suv = "suv";            // شاسی‌بلند
    public const string Van = "van";            // ون
    public const string Pickup = "pickup";      // وانت
    public const string Motorcycle = "motorcycle";
    /// <summary>Legacy / unspecified.</summary>
    public const string Vehicle = "vehicle";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Car, Suv, Van, Pickup, Motorcycle, Vehicle };
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

/// <summary>Keeps <see cref="Asset.Title"/> and brand/model attributes aligned when the user types off-catalog names.</summary>
public static class VehicleAssetAttributes
{
    public static Dictionary<string, string>? EnsureBrandModel(string title, Dictionary<string, string>? attributes)
    {
        if (string.IsNullOrWhiteSpace(title)) return attributes is { Count: > 0 } ? attributes : null;
        attributes ??= new Dictionary<string, string>();
        var t = title.Trim();
        var hasBrand = attributes.TryGetValue("brand", out var brand) && !string.IsNullOrWhiteSpace(brand);
        var hasModel = attributes.TryGetValue("model", out var model) && !string.IsNullOrWhiteSpace(model);
        if (hasBrand && hasModel) return attributes;

        if (hasBrand && !hasModel && t.StartsWith(brand!.Trim(), StringComparison.Ordinal))
            attributes["model"] = t[brand.Trim().Length..].Trim();
        else if (!hasBrand && hasModel && t.EndsWith(model!.Trim(), StringComparison.Ordinal) && t.Length > model.Trim().Length)
            attributes["brand"] = t[..^model.Trim().Length].Trim();
        else
        {
            var space = t.IndexOf(' ');
            if (space > 0)
            {
                if (!hasBrand) attributes["brand"] = t[..space];
                if (!hasModel) attributes["model"] = t[(space + 1)..];
            }
            else if (!hasModel)
                attributes["model"] = t;
        }

        return attributes;
    }
}
