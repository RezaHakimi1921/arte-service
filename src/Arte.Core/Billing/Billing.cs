using Arte.Core.Common;
using Arte.Core.Tenancy;

namespace Arte.Core.Billing;

public static class ItemKinds
{
    public const string Part = "part";        // قطعه
    public const string Labor = "labor";      // اجرت
    public const string Service = "service";  // خدمت (بسته یا خدمت ثابت)

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Part, Labor, Service };
}

public static class Suppliers
{
    public const string Shop = "shop";
    /// <summary>The customer brought the part: billed at zero, kept in the vehicle's history, no shop warranty.</summary>
    public const string Customer = "customer";
}

public static class ItemStatuses
{
    /// <summary>Needed but not fitted yet (e.g. waiting for the part); not billed.</summary>
    public const string Needed = "needed";
    public const string Used = "used";
}

public static class PaymentMethods
{
    public const string Cash = "cash";
    public const string Card = "card";          // کارت‌خوان
    public const string Transfer = "transfer";  // کارت به کارت / انتقال
    public const string Other = "other";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Cash, Card, Transfer, Other };
}

/// <summary>A part, labor or service on a case. Money is in rials.</summary>
public sealed class CaseItem : ITenantOwned, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid CaseId { get; set; }
    public required string Kind { get; set; }
    public required string Title { get; set; }
    public decimal Quantity { get; set; } = 1;
    /// <summary>Sale price per unit (what the customer pays).</summary>
    public long UnitPriceRials { get; set; }
    /// <summary>Purchase price per unit. Only owners / reports.view ever see it.</summary>
    public long? UnitCostRials { get; set; }
    public long DiscountRials { get; set; }
    public string Supplier { get; set; } = Suppliers.Shop;
    public string Status { get; set; } = ItemStatuses.Used;
    /// <summary>Membership of the staff member who did the labor (wage / commission reports).</summary>
    public Guid? PerformedBy { get; set; }
    public int? WarrantyDays { get; set; }
    public Guid? CatalogItemId { get; set; }
    public Guid AddedBy { get; set; }
    public DateTimeOffset AddedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    public bool Billable => Status == ItemStatuses.Used && Supplier == Suppliers.Shop;

    /// <summary>What the customer is charged for this line.</summary>
    public long LineTotalRials => Billable ? Math.Max(0, (long)Math.Round(Quantity * UnitPriceRials) - DiscountRials) : 0;

    public long LineCostRials => Billable && UnitCostRials is { } c ? (long)Math.Round(Quantity * c) : 0;
}

public sealed class Payment : ITenantOwned, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid CaseId { get; set; }
    public Guid CustomerId { get; set; }
    public long AmountRials { get; set; }
    public required string Method { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public Guid RecordedBy { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}

/// <summary>Price list the shop reuses; free-text lines are always allowed too.</summary>
public sealed class CatalogItem : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string Kind { get; set; }
    public required string Title { get; set; }
    public long DefaultPriceRials { get; set; }
    public long? DefaultCostRials { get; set; }
    public int? DefaultWarrantyDays { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>This business's price for a shared starter item; updated to the last price used.</summary>
    public Guid? StandardItemId { get; set; }
}

public sealed record CaseMoney(long TotalRials, long PaidRials, long BalanceRials, long PartsRials, long LaborRials,
    long ServicesRials, long CostRials, long ProfitRials)
{
    public static CaseMoney Of(IEnumerable<CaseItem> items, IEnumerable<Payment> payments)
    {
        var billable = items.Where(i => i.DeletedAt == null).ToList();
        var total = billable.Sum(i => i.LineTotalRials);
        var paid = payments.Where(p => p.DeletedAt == null).Sum(p => p.AmountRials);
        var cost = billable.Sum(i => i.LineCostRials);
        return new CaseMoney(
            total, paid, total - paid,
            billable.Where(i => i.Kind == ItemKinds.Part).Sum(i => i.LineTotalRials),
            billable.Where(i => i.Kind == ItemKinds.Labor).Sum(i => i.LineTotalRials),
            billable.Where(i => i.Kind == ItemKinds.Service).Sum(i => i.LineTotalRials),
            cost, total - cost);
    }
}
