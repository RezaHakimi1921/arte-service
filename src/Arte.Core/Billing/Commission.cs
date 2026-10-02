using Arte.Core.Identity;

namespace Arte.Core.Billing;

public static class CommissionTypes
{
    public const string None = "none";
    /// <summary>A percentage of the commission base.</summary>
    public const string Percent = "percent";
    /// <summary>A fixed amount for every delivered case.</summary>
    public const string FixedPerCase = "fixed_per_case";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { None, Percent, FixedPerCase };
}

public static class CommissionBases
{
    /// <summary>Everything billed on the case (parts + labor + services). The default.</summary>
    public const string CaseTotal = "case_total";
    /// <summary>Only the work: labor and service lines.</summary>
    public const string Labor = "labor";
    /// <summary>Work plus the profit on parts (sale − purchase; parts without a purchase price count as zero profit).</summary>
    public const string LaborPlusPartsProfit = "labor_plus_parts_profit";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { CaseTotal, Labor, LaborPlusPartsProfit };
}

/// <summary>
/// What a staff member earns from one delivered case they were responsible for.
/// Only billable lines count (shop-supplied and fitted), exactly like the customer's bill.
/// Example (spec 08): parts sold 3,000,000 (bought 2,500,000) + labor 1,000,000, 20%:
/// case total → 800,000 · labor → 200,000 · labor + parts profit → 300,000.
/// </summary>
public static class Commission
{
    public sealed record Result(long BaseRials, long CommissionRials);

    public static long Base(IEnumerable<CaseItem> items, string commissionBase)
    {
        var lines = items.Where(i => i.DeletedAt == null && i.Billable).ToList();
        var work = lines.Where(i => i.Kind is ItemKinds.Labor or ItemKinds.Service).Sum(i => i.LineTotalRials);
        return commissionBase switch
        {
            CommissionBases.Labor => work,
            CommissionBases.LaborPlusPartsProfit => work + lines
                .Where(i => i.Kind == ItemKinds.Part && i.UnitCostRials != null)
                .Sum(i => Math.Max(0, i.LineTotalRials - i.LineCostRials)),
            _ => lines.Sum(i => i.LineTotalRials),
        };
    }

    public static Result For(IEnumerable<CaseItem> items, Membership m)
    {
        var baseRials = Base(items, m.CommissionBase);
        var commission = m.CommissionType switch
        {
            CommissionTypes.Percent when m.CommissionPercent is { } p => (long)Math.Round(baseRials * p / 100m, MidpointRounding.AwayFromZero),
            CommissionTypes.FixedPerCase => m.CommissionFixedRials ?? 0,
            _ => 0,
        };
        return new Result(baseRials, commission);
    }
}
