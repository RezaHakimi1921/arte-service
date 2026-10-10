namespace Arte.Core.Billing;

/// <summary>
/// A common good or job for one group of vehicles («روغن موتور», «تعویض لنت»), kept by the platform admin and
/// shared by every business. It has no price: each business's price lives in its own CatalogItem (StandardItemId),
/// remembered from the last time it was used. Never deleted (cases and catalogs point at it); switched off instead.
/// </summary>
public sealed class StandardItem
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>part (کالای مصرف‌شده) or labor (اجرت و خدمات).</summary>
    public required string Kind { get; set; }
    public required string Title { get; set; }
    /// <summary>Group heading in pickers: «روغن و فیلتر», «ترمز», «برق»…</summary>
    public required string Category { get; set; }
    /// <summary>Asset kinds it applies to (motorcycle, or the car kinds).</summary>
    public string[] VehicleKinds { get; set; } = [];
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A ready-made set of lines («تعویض روغن», «سرویس دوره‌ای») added to a case with one tap; prices come from the business.</summary>
public sealed class ServicePackage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string[] VehicleKinds { get; set; } = [];
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class ServicePackageLine
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid PackageId { get; set; }
    public Guid StandardItemId { get; set; }
    public decimal Quantity { get; set; } = 1;
    /// <summary>Offered but not ticked (e.g. brake fluid in a brake service).</summary>
    public bool Optional { get; set; }
    public int SortOrder { get; set; }
}

public static class VehicleGroups
{
    public static readonly string[] Motorcycle = ["motorcycle"];
    public static readonly string[] Car = ["car", "suv", "van", "pickup"];
}
