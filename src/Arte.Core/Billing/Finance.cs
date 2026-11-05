using Arte.Core.Common;
using Arte.Core.Tenancy;

namespace Arte.Core.Billing;

/// <summary>A card reader (کارتخوان) of the business. Deactivated, never deleted, so old payments keep their reader.</summary>
public sealed class PaymentTerminal : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string Name { get; set; }
    public string? Bank { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A bank card customers can pay into (card to card). One can be shown on the customer's tracking page.</summary>
public sealed class BankCard : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    /// <summary>16 digits, Latin.</summary>
    public required string CardNumber { get; set; }
    public required string HolderName { get; set; }
    public string? Bank { get; set; }
    public bool IsActive { get; set; } = true;
    public bool ShowOnTracking { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public static class ReceiptStatuses
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Rejected = "rejected";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Pending, Approved, Rejected };
}

/// <summary>A photo of a bank receipt sent by the customer from the tracking page; the shop approves (recording the payment) or rejects it.</summary>
public sealed class CustomerReceipt : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid CaseId { get; set; }
    public Guid AttachmentId { get; set; }
    /// <summary>What the customer says they paid; the approver confirms or corrects it.</summary>
    public long? ClaimedRials { get; set; }
    public string Status { get; set; } = ReceiptStatuses.Pending;
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    /// <summary>The payment recorded when the receipt was approved.</summary>
    public Guid? PaymentId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One browser or phone of a staff member that receives push notifications.</summary>
public sealed class PushSubscription : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    public required string Endpoint { get; set; }
    public required string P256dh { get; set; }
    public required string Auth { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
