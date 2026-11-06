using Arte.Core.Common;
using Arte.Core.Tenancy;

namespace Arte.Core.Workshop;

/// <summary>Motorcycle kinds that have their own intake checklist: geared (دنده‌ای) and automatic / scooter (کلیک).</summary>
public static class MotorTypes
{
    public const string Geared = "geared";
    public const string Automatic = "automatic";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Geared, Automatic };
}

public static class IntakeOptionKinds
{
    public const string Problem = "problem";   // ایراد اعلامی
    public const string Service = "service";   // خدمات درخواستی
    public const string Category = "category"; // دسته‌بندی اصلی ردیف‌های چک‌لیست
    public const string ServiceCategory = "service_category"; // دسته‌بندی خدمات درخواستی

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Problem, Service, Category, ServiceCategory };
}

/// <summary>
/// One entry of the business's own list of reported problems or requested services, edited in Settings.
/// Deactivated (never deleted) so old cases keep their wording. A business with no entries uses the built-in lists.
/// </summary>
public sealed class IntakeOption : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string Kind { get; set; }
    /// <summary>Category heading for services (e.g. «ترمز»); empty for problems.</summary>
    public string? Group { get; set; }
    public required string Title { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>One row of the intake checklist the business ticks when a motorcycle is received (one list per motor type).</summary>
public sealed class ChecklistTemplateItem : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string MotorType { get; set; }
    public string? Group { get; set; }
    public required string Title { get; set; }
    /// <summary>A required row must be ticked before the case can be opened.</summary>
    public bool IsRequired { get; set; } = true;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A checklist row copied onto the case when it is opened, so editing the template later never changes old cases.
/// </summary>
public sealed class CaseChecklistItem : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid CaseId { get; set; }
    public Guid? TemplateItemId { get; set; }
    public string? Group { get; set; }
    public required string Title { get; set; }
    public bool IsRequired { get; set; }
    public bool IsChecked { get; set; }
    /// <summary>What the person found on this point (optional).</summary>
    public string? Note { get; set; }
    public int SortOrder { get; set; }
    public Guid? CheckedBy { get; set; }
    public DateTimeOffset? CheckedAt { get; set; }
}

public static class FindingSources
{
    /// <summary>The customer told us (also kept on the case as the reported problems).</summary>
    public const string Customer = "customer";
    /// <summary>The mechanic found it while working.</summary>
    public const string Mechanic = "mechanic";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Customer, Mechanic };
}

/// <summary>
/// A problem recorded on a case at any time (typically one the mechanic found), kept as data so the business can later see
/// which problems come up most, per motor type and month.
/// </summary>
public sealed class CaseFinding : ITenantOwned, ISoftDeletable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid CaseId { get; set; }
    public required string Source { get; set; }
    public required string Title { get; set; }
    public string? Note { get; set; }
    public Guid? OptionId { get; set; }
    public string? MotorType { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
}
