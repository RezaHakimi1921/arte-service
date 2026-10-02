namespace Arte.Core.Identity;

public static class Permissions
{
    public const string CasesCreate = "cases.create";
    public const string CasesAssign = "cases.assign";
    public const string CasesViewAll = "cases.view_all";
    public const string CasesWork = "cases.work";
    public const string PaymentsRecord = "payments.record";
    public const string ReportsView = "reports.view";
    public const string StaffManage = "staff.manage";
    public const string SettingsManage = "settings.manage";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        CasesCreate, CasesAssign, CasesViewAll, CasesWork,
        PaymentsRecord, ReportsView, StaffManage, SettingsManage,
    };
}

public static class Roles
{
    public const string Owner = "owner";
    public const string Supervisor = "supervisor";
    public const string Technician = "technician";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Owner, Supervisor, Technician };

    /// <summary>A role only seeds the permissions; the owner can change them per member.</summary>
    public static string[] DefaultPermissions(string role) => role switch
    {
        Owner => [.. Permissions.All],
        Supervisor => [Permissions.CasesCreate, Permissions.CasesAssign, Permissions.CasesViewAll, Permissions.CasesWork],
        Technician => [Permissions.CasesWork],
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };
}

public static class PayModels
{
    public const string None = "none";
    public const string Fixed = "fixed";
    public const string Commission = "commission";
    public const string Mixed = "mixed";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { None, Fixed, Commission, Mixed };
}
