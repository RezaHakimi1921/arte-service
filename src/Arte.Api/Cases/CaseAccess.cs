using Arte.Core.Cases;
using Arte.Core.Identity;
using Arte.Core.Workflows;

namespace Arte.Api.Cases;

/// <summary>
/// Who may see and move a case.
/// - cases.view_all or cases.create: every case of the business.
/// - otherwise: only cases assigned to you. A technician closes their own work ("پایان تعمیر")
///   without the owner being there.
/// </summary>
public static class CaseAccess
{
    public static bool CanSeeAll(Membership m) => m.Has(Permissions.CasesViewAll) || m.Has(Permissions.CasesCreate);

    public static bool CanSee(Membership m, Case c) => CanSeeAll(m) || c.AssigneeId == m.Id;

    public static bool CanWorkOn(Membership m, Case c) =>
        m.Has(Permissions.CasesWork) && (CanSeeAll(m) || c.AssigneeId == m.Id);

    public static bool CanRun(Membership m, Case c, Transition t) =>
        t.RequiredPermission == Permissions.CasesWork ? CanWorkOn(m, c) : m.Has(t.RequiredPermission) && CanSee(m, c);
}
