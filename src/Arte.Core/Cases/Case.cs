using System.Text.Json;
using Arte.Core.Common;
using Arte.Core.Tenancy;

namespace Arte.Core.Cases;

public static class CaseRelations
{
    /// <summary>The customer came back with the same problem (warranty / رجوعی).</summary>
    public const string Comeback = "comeback";
    public const string FollowUp = "follow_up";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Comeback, FollowUp };
}

/// <summary>Why work on a case is stopped. Shown to people instead of a bare "waiting".</summary>
public static class WaitReasons
{
    public const string CustomerApproval = "customer_approval";   // منتظر تأیید هزینه توسط مشتری
    public const string CustomerParts = "customer_parts";         // منتظر قطعه‌ای که مشتری می‌آورد
    public const string ShopParts = "shop_parts";                 // منتظر تأمین قطعه توسط تعمیرگاه
    public const string OwnerDecision = "owner_decision";         // منتظر تصمیم استاد
    public const string Payment = "payment";                      // منتظر پرداخت
    public const string Customer = "customer";                    // منتظر مشتری (پاسخ، آوردن وسیله…)

    public static readonly IReadOnlySet<string> All = new HashSet<string>
        { CustomerApproval, CustomerParts, ShopParts, OwnerDecision, Payment, Customer };

    public static readonly IReadOnlySet<string> Parts = new HashSet<string> { CustomerParts, ShopParts };
}

public static class BodyStatuses
{
    public const string Ok = "ok";
    public const string Damaged = "damaged";
}

public static class CustodyStatuses
{
    public const string InShop = "in_shop";
    public const string WithCustomer = "with_customer";
}

/// <summary>پرونده: one job for one customer (and usually one motorcycle), moving through the business workflow.</summary>
public sealed class Case : ITenantOwned, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public long Number { get; set; }

    public Guid CustomerId { get; set; }
    public Guid? AssetId { get; set; }
    public Guid WorkflowId { get; set; }
    public Guid StageId { get; set; }
    /// <summary>Membership (not user) of the staff member the job is assigned to.</summary>
    public Guid? AssigneeId { get; set; }

    public required string Request { get; set; }
    public string? Diagnosis { get; set; }
    public int? OdometerKm { get; set; }
    public long? EstimatedAmountRials { get; set; }
    public DateTimeOffset? PromisedAt { get; set; }

    /// <summary>Services the customer asked for, picked from the categorised list (no free text).</summary>
    public string[] RequestedServices { get; set; } = [];
    /// <summary>Problems the customer reported («ایراد اعلامی»), picked from a list; details go in Request.</summary>
    public string[] ReportedProblems { get; set; } = [];
    /// <summary>0 empty … 4 full, null = not checked.</summary>
    public short? FuelLevel { get; set; }
    /// <summary>ok | damaged, null = not checked.</summary>
    public string? BodyStatus { get; set; }
    public string? BodyNotes { get; set; }

    /// <summary>Why the job is stopped (any stage), null when it is moving. See WaitReasons.</summary>
    public string? WaitReason { get; set; }

    public string CustodyStatus { get; set; } = CustodyStatuses.InShop;
    /// <summary>What came in with the motorcycle: helmet, key, papers, visible damage…</summary>
    public JsonDocument? IntakeChecklist { get; set; }

    public Guid? ParentCaseId { get; set; }
    public string? Relation { get; set; }
    public Guid? MergedIntoCaseId { get; set; }

    /// <summary>Set at delivery from the longest warranty on shop-supplied lines.</summary>
    public DateTimeOffset? WarrantyUntil { get; set; }
    /// <summary>Delivered with a balance (نسیه): when the customer promised to pay.</summary>
    public DateTimeOffset? CreditDueAt { get; set; }

    public DateTimeOffset OpenedAt { get; set; }
    public DateTimeOffset StageEnteredAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public Guid OpenedBy { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }

    /// <summary>Postgres xmin: two people cannot move the same case at the same moment.</summary>
    public uint Version { get; set; }
}

/// <summary>Timeline entry. Append-only: never updated or deleted.</summary>
public sealed class CaseEvent : ITenantOwned
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid CaseId { get; set; }
    public required string Type { get; set; }
    public Guid? ActorUserId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public JsonDocument? Data { get; set; }
}

public static class CaseEventTypes
{
    public const string Opened = "case.opened";
    public const string StageChanged = "case.stage_changed";
    public const string Assigned = "case.assigned";
    public const string Updated = "case.updated";
    public const string NoteAdded = "case.note_added";
    public const string Reopened = "case.reopened";
    public const string Delivered = "case.delivered";
    public const string Cancelled = "case.cancelled";
    public const string Deleted = "case.deleted";
    public const string Restored = "case.restored";
    public const string CustodyChanged = "case.custody_changed";
    public const string WaitChanged = "case.wait_changed";
    public const string ItemAdded = "case.item_added";
    public const string ItemUpdated = "case.item_updated";
    public const string ItemRemoved = "case.item_removed";
    public const string PaymentRecorded = "payment.recorded";
    public const string PaymentVoided = "payment.voided";
    public const string Credit = "case.credit";
}
