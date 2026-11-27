using Arte.Core.Tenancy;

namespace Arte.Core.Billing;

public static class DeductionCategories
{
    public const string Advance = "advance";
    public const string FinancialAdjustment = "financial_adjustment";
    public const string LossLinked = "loss_linked";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Advance, FinancialAdjustment, LossLinked };
}

public static class StaffPayEntityTypes
{
    public const string Bonus = "bonus";
    public const string Deduction = "deduction";
    public const string Settlement = "settlement";
}

public static class StaffPayActions
{
    public const string Create = "create";
    public const string Edit = "edit";
    public const string Void = "void";
}

/// <summary>
/// Commission earned on the first delivery of a case. One row per case: reopen and a later delivery do not add or remove it.
/// Amounts are the snapshot of <see cref="Commission.For"/> for the assignee at that moment.
/// </summary>
public sealed class CaseCommissionGrant : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid CaseId { get; set; }
    public Guid MembershipId { get; set; }
    public long BaseRials { get; set; }
    public long CommissionRials { get; set; }
    public required string CommissionType { get; set; }
    public decimal? CommissionPercent { get; set; }
    public long? CommissionFixedRials { get; set; }
    public required string CommissionBase { get; set; }
    public DateTimeOffset GrantedAt { get; set; }
}

/// <summary>A bonus for one Jalali month. Several per month are allowed. Void hides it from totals; the row stays.</summary>
public sealed class StaffBonus : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid MembershipId { get; set; }
    public long AmountRials { get; set; }
    public required string Title { get; set; }
    public int JalaliYear { get; set; }
    public int JalaliMonth { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
}

/// <summary>
/// Money taken off one Jalali month's income (advance, adjustment, or an explicit link to a shop loss).
/// The shop-loss report itself is never deducted; this row is only when someone records a deduction.
/// </summary>
public sealed class StaffDeduction : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid MembershipId { get; set; }
    public required string Category { get; set; }
    public long AmountRials { get; set; }
    /// <summary>Why this deduction was recorded (required on create).</summary>
    public required string Reason { get; set; }
    public int JalaliYear { get; set; }
    public int JalaliMonth { get; set; }
    public Guid? ShopLossId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? VoidedAt { get; set; }
    public Guid? VoidedBy { get; set; }
    public string? VoidReason { get; set; }
}

/// <summary>Append-only trail for bonus, deduction, and settlement create / edit / void. Rows are never updated.</summary>
public sealed class StaffPayAuditLog : ITenantOwned
{
    public long Id { get; set; }
    public Guid TenantId { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public required string Action { get; set; }
    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? Reason { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// One confirmed snapshot per person per Jalali month. Shop loss is stored beside pay and is not subtracted from
/// <see cref="AfterDeductionsRials"/> (that figure is income minus deductions only).
/// </summary>
public sealed class StaffPaySettlement : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid MembershipId { get; set; }
    public int JalaliYear { get; set; }
    public int JalaliMonth { get; set; }
    public long FixedSalaryRials { get; set; }
    public long CommissionRials { get; set; }
    public long BonusRials { get; set; }
    public long DeductionRials { get; set; }
    public long ShopLossRials { get; set; }
    public long IncomeRials { get; set; }
    public long AfterDeductionsRials { get; set; }
    public DateTimeOffset ConfirmedAt { get; set; }
    public Guid ConfirmedBy { get; set; }
}
