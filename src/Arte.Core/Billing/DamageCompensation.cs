using Arte.Core.Cases;

namespace Arte.Core.Billing;

/// <summary>Maps compensation parts to discount lines per product doc 32 §5.</summary>
public static class DamageCompensation
{
    public static long LineGrossRials(CaseItem item) =>
        (long)Math.Round(item.Quantity * item.UnitPriceRials);

    /// <summary>Credit applied on a used compensation line.</summary>
    public static long AppliedCreditRials(CaseItem item)
    {
        if (item.CustomerDamageId is null || item.DeletedAt is not null || item.Status != ItemStatuses.Used)
            return 0;
        return Math.Max(0, item.CompensationCreditRials);
    }

    public static long RemainingCreditRials(long reportAmountRials, IEnumerable<CaseItem> linkedItems)
    {
        var used = linkedItems.Sum(AppliedCreditRials);
        return Math.Max(0, reportAmountRials - used);
    }

    public static void ApplyToItem(CaseItem item, long remainingCredit, string? excessMode, out long shopLossExcessRials)
    {
        shopLossExcessRials = 0;
        var gross = LineGrossRials(item);
        var applied = Math.Min(gross, Math.Max(0, remainingCredit));
        var excess = gross - applied;
        long billToCustomer;
        if (excess <= 0)
            billToCustomer = 0;
        else if (excessMode == CompensationExcessModes.ShopLoss)
        {
            billToCustomer = 0;
            shopLossExcessRials = excess;
        }
        else
            billToCustomer = excess;

        item.DiscountRials = gross - billToCustomer;
        item.CompensationCreditRials = applied;
        item.CompensationExcessMode = excess > 0 ? excessMode : null;
    }

    public static void ClearCompensation(CaseItem item)
    {
        item.CustomerDamageId = null;
        item.CompensationExcessMode = null;
        item.CompensationCreditRials = 0;
        item.CompensationShopLossId = null;
    }
}