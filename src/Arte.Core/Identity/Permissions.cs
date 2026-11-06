namespace Arte.Core.Identity;

public static class Permissions
{
    public const string CasesCreate = "cases.create";
    public const string CasesAssign = "cases.assign";
    public const string CasesViewAll = "cases.view_all";
    public const string CasesWork = "cases.work";
    /// <summary>Registering a new intake (پذیرش). Whoever can manage cases (CasesCreate) can always intake too.</summary>
    public const string CasesIntake = "cases.intake";
    public const string PaymentsRecord = "payments.record";
    public const string ReportsView = "reports.view";
    public const string StaffManage = "staff.manage";
    public const string SettingsManage = "settings.manage";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        CasesCreate, CasesAssign, CasesViewAll, CasesWork, CasesIntake,
        PaymentsRecord, ReportsView, StaffManage, SettingsManage,
    };
}

public static class Roles
{
    public const string Owner = "owner";
    public const string Supervisor = "supervisor";
    public const string Technician = "technician";
    /// <summary>کارآموز: sees the panel, changes nothing (read-only).</summary>
    public const string Apprentice = "apprentice";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Owner, Supervisor, Technician, Apprentice };

    /// <summary>A role only seeds the permissions; the owner can change them per member.</summary>
    public static string[] DefaultPermissions(string role) => role switch
    {
        Owner => [.. Permissions.All],
        // مکانیک ارشد: approves, sees every case and the reports; no settings, no staff management.
        Supervisor => [Permissions.CasesCreate, Permissions.CasesAssign, Permissions.CasesViewAll, Permissions.CasesWork, Permissions.CasesIntake, Permissions.ReportsView],
        // مکانیک: takes cars in and does the work on his own jobs.
        Technician => [Permissions.CasesWork, Permissions.CasesIntake],
        Apprentice => [Permissions.CasesViewAll],
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
