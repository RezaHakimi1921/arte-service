using Arte.Core.Common;
using Arte.Core.Tenancy;

namespace Arte.Core.Cases;

/// <summary>Shop credit owed to the customer after damage on their vehicle (not customer debt).</summary>
public sealed class CustomerDamageReport : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid CaseId { get; set; }
    public long AmountRials { get; set; }
    public string? InternalNote { get; set; }
    public bool ShowReportToCustomer { get; set; }
    public bool ShowMediaToCustomer { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }
    public string? VoidReason { get; set; }

    public bool IsActive => VoidedAt is null;
}

/// <summary>Internal shop loss (optional case link). Not shown to customers.</summary>
public sealed class ShopLossReport : ITenantOwned, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid? CaseId { get; set; }
    public long AmountRials { get; set; }
    public string? InternalNote { get; set; }
    public Guid ResponsibleUserId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}

public static class CompensationExcessModes
{
    public const string CustomerPurchase = "customer_purchase";
    public const string ShopLoss = "shop_loss";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { CustomerPurchase, ShopLoss };
}