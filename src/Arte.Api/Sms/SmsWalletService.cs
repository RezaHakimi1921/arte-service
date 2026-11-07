using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Sms;

/// <summary>A rule of the SMS credit was broken; Code is stable for the API, the message is Persian for the admin.</summary>
public sealed class SmsWalletException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>Parts held back for one message; handed back if the message is not accepted.</summary>
public sealed record SmsReservation(Guid? TenantId, long Parts, string MessageKey);

public sealed record SmsWalletStatus(
    long CentralParts, long CommittedParts, long HeadroomParts,
    long PurchasePricePerPartRial, long SalePricePerPartRial, long ProfitPerPartRial, long LowCentralParts,
    IReadOnlyList<string> Warnings);

/// <summary>
/// SMS credit in parts. Two levels: the central balance (what Arte holds at SmartX) and a balance per branch.
/// Rules, all enforced here and in no other place:
///  • a branch can never be given more than the central balance still leaves free (sum of branches ≤ central);
///  • a branch's message lowers its own balance and the central balance by the same parts;
///  • a message Arte pays for (the login code, tenant = null) lowers only the central balance;
///  • a branch that cannot pay is refused; the login code is refused only when the central balance is empty;
///  • a message that is not accepted gives its parts back.
/// Spending is a single conditional UPDATE, so two messages can never spend the same parts.
/// </summary>
public sealed class SmsWalletService(ArteDbContext db, IClock clock, ILogger<SmsWalletService> logger)
{
    public const string InsufficientCredit = "insufficient_credit";
    public const string CentralInsufficient = "central_insufficient";

    public async Task<SmsWalletStatus> StatusAsync(CancellationToken ct)
    {
        var s = await db.SmsSettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);
        var committed = await db.SmsWallets.AsNoTracking().SumAsync(w => (long?)w.BalanceParts, ct) ?? 0;
        var warnings = new List<string>();
        if (committed > s.CentralBalanceParts) warnings.Add("central_exceeded");
        if (s.CentralBalanceParts <= s.LowCentralParts) warnings.Add("central_low");
        return new SmsWalletStatus(s.CentralBalanceParts, committed, s.CentralBalanceParts - committed,
            s.PurchasePricePerPartRial, s.SalePricePerPartRial, s.SalePricePerPartRial - s.PurchasePricePerPartRial,
            s.LowCentralParts, warnings);
    }

    public async Task<long> BranchBalanceAsync(Guid tenantId, CancellationToken ct) =>
        await db.SmsWallets.AsNoTracking().Where(w => w.TenantId == tenantId).Select(w => (long?)w.BalanceParts).SingleOrDefaultAsync(ct) ?? 0;

    // ───────── spending ─────────

    public async Task<(SmsReservation? Reservation, string? Error)> ReserveAsync(Guid? tenantId, long parts, string messageKey, CancellationToken ct)
    {
        if (parts <= 0) return (new SmsReservation(tenantId, 0, messageKey), null);
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        long? branchAfter = null;
        if (tenantId is { } t)
        {
            var n = await db.SmsWallets.Where(w => w.TenantId == t && w.BalanceParts >= parts)
                .ExecuteUpdateAsync(u => u.SetProperty(w => w.BalanceParts, w => w.BalanceParts - parts).SetProperty(w => w.UpdatedAt, now), ct);
            if (n == 0) return (null, InsufficientCredit);
            branchAfter = await db.SmsWallets.Where(w => w.TenantId == t).Select(w => w.BalanceParts).SingleAsync(ct);
        }

        var c = await db.SmsSettings.Where(x => x.Id == 1 && x.CentralBalanceParts >= parts)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.CentralBalanceParts, x => x.CentralBalanceParts - parts).SetProperty(x => x.UpdatedAt, now), ct);
        if (c == 0) return (null, CentralInsufficient);   // the transaction is dropped: the branch parts come back too
        var s = await db.SmsSettings.AsNoTracking().SingleAsync(x => x.Id == 1, ct);

        db.SmsLedger.Add(new SmsLedgerEntry
        {
            TenantId = tenantId, Kind = tenantId is null ? SmsLedgerKinds.SpendArte : SmsLedgerKinds.Spend,
            Parts = -parts, BalanceAfter = branchAfter, CentralAfter = s.CentralBalanceParts, MessageKey = messageKey, CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        if (s.CentralBalanceParts <= s.LowCentralParts)
            logger.LogWarning("SMS central balance is low: {Parts} parts left. Charge the SmartX panel and record it in the admin panel.", s.CentralBalanceParts);
        return (new SmsReservation(tenantId, parts, messageKey), null);
    }

    public async Task RefundAsync(SmsReservation r, CancellationToken ct = default)
    {
        if (r.Parts <= 0) return;
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        long? branchAfter = null;
        if (r.TenantId is { } t)
        {
            await db.SmsWallets.Where(w => w.TenantId == t)
                .ExecuteUpdateAsync(u => u.SetProperty(w => w.BalanceParts, w => w.BalanceParts + r.Parts).SetProperty(w => w.UpdatedAt, now), ct);
            branchAfter = await db.SmsWallets.Where(w => w.TenantId == t).Select(w => w.BalanceParts).SingleAsync(ct);
        }
        await db.SmsSettings.Where(x => x.Id == 1)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.CentralBalanceParts, x => x.CentralBalanceParts + r.Parts).SetProperty(x => x.UpdatedAt, now), ct);
        var central = await db.SmsSettings.Where(x => x.Id == 1).Select(x => x.CentralBalanceParts).SingleAsync(ct);
        db.SmsLedger.Add(new SmsLedgerEntry
        {
            TenantId = r.TenantId, Kind = SmsLedgerKinds.Refund, Parts = r.Parts, BalanceAfter = branchAfter,
            CentralAfter = central, MessageKey = r.MessageKey, CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ───────── admin ─────────

    /// <summary>Parts for an amount paid, at the given price per part (whole parts only).</summary>
    public static long PartsFor(long amountRial, long pricePerPartRial) =>
        pricePerPartRial <= 0 ? 0 : amountRial / pricePerPartRial;

    /// <summary>Arte bought credit at SmartX: the central balance grows.</summary>
    public async Task<long> TopUpCentralAsync(long parts, long? amountRial, string? note, Guid? by, CancellationToken ct)
    {
        if (parts <= 0) throw new SmsWalletException("invalid_parts", "تعداد پارت باید بیشتر از صفر باشد.");
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.SmsSettings.Where(x => x.Id == 1)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.CentralBalanceParts, x => x.CentralBalanceParts + parts).SetProperty(x => x.UpdatedAt, now), ct);
        var central = await db.SmsSettings.Where(x => x.Id == 1).Select(x => x.CentralBalanceParts).SingleAsync(ct);
        db.SmsLedger.Add(new SmsLedgerEntry { Kind = SmsLedgerKinds.CentralTopUp, Parts = parts, CentralAfter = central, AmountRial = amountRial, Note = Cut(note), CreatedBy = by, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return central;
    }

    /// <summary>The central balance is set to what the SmartX panel shows (until SmartX offers a balance API, by hand).</summary>
    public async Task<long> SyncCentralAsync(long parts, string? note, Guid? by, CancellationToken ct)
    {
        if (parts < 0) throw new SmsWalletException("invalid_parts", "موجودی نمی‌تواند منفی باشد.");
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var before = await db.SmsSettings.Where(x => x.Id == 1).Select(x => x.CentralBalanceParts).SingleAsync(ct);
        await db.SmsSettings.Where(x => x.Id == 1)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.CentralBalanceParts, parts).SetProperty(x => x.UpdatedAt, now), ct);
        db.SmsLedger.Add(new SmsLedgerEntry { Kind = SmsLedgerKinds.CentralSync, Parts = parts - before, CentralAfter = parts, Note = Cut(note), CreatedBy = by, CreatedAt = now });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return parts;
    }

    /// <summary>
    /// Gives a branch credit (negative: takes it back). Refused when the branches together would hold more than the
    /// central balance, so a customer can never be promised credit Arte has not bought.
    /// </summary>
    public async Task<long> CreditBranchAsync(Guid tenantId, long deltaParts, long? amountRial, string? note, Guid? by, CancellationToken ct)
    {
        if (deltaParts == 0) throw new SmsWalletException("invalid_parts", "تعداد پارت نباید صفر باشد.");
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct)) throw new SmsWalletException("not_found", "شعبه پیدا نشد.");
        var now = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // Taking the settings row's lock first makes two simultaneous gifts check the headroom one after the other.
        await db.SmsSettings.Where(x => x.Id == 1).ExecuteUpdateAsync(u => u.SetProperty(x => x.UpdatedAt, now), ct);
        var central = await db.SmsSettings.Where(x => x.Id == 1).Select(x => x.CentralBalanceParts).SingleAsync(ct);
        var committed = await db.SmsWallets.SumAsync(w => (long?)w.BalanceParts, ct) ?? 0;

        var wallet = await db.SmsWallets.SingleOrDefaultAsync(w => w.TenantId == tenantId, ct);
        if (wallet is null)
        {
            wallet = new TenantSmsWallet { TenantId = tenantId, BalanceParts = 0, UpdatedAt = now };
            db.SmsWallets.Add(wallet);
        }
        if (deltaParts > 0 && committed + deltaParts > central)
            throw new SmsWalletException("exceeds_central",
                $"شارژ دفتر مرکزی کافی نیست: آزاد {Math.Max(0, central - committed):N0} پارت است. اول پنل اسمارت را شارژ و موجودی مرکزی را ثبت کنید.");
        if (wallet.BalanceParts + deltaParts < 0)
            throw new SmsWalletException("negative_balance", $"موجودی شعبه {wallet.BalanceParts:N0} پارت است؛ بیشتر از آن برداشت نمی‌شود.");

        wallet.BalanceParts += deltaParts;
        wallet.UpdatedAt = now;
        db.SmsLedger.Add(new SmsLedgerEntry
        {
            TenantId = tenantId, Kind = SmsLedgerKinds.Credit, Parts = deltaParts, BalanceAfter = wallet.BalanceParts,
            CentralAfter = central, AmountRial = amountRial, Note = Cut(note), CreatedBy = by, CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return wallet.BalanceParts;
    }

    public async Task SetPricesAsync(long purchaseRial, long saleRial, long? lowCentralParts, CancellationToken ct)
    {
        if (purchaseRial <= 0 || saleRial <= 0) throw new SmsWalletException("invalid_price", "قیمت باید بیشتر از صفر باشد.");
        if (saleRial < purchaseRial) throw new SmsWalletException("loss", "قیمت فروش نمی‌تواند از قیمت خرید کمتر باشد.");
        var row = await db.SmsSettings.SingleAsync(x => x.Id == 1, ct);
        row.PurchasePricePerPartRial = purchaseRial;
        row.SalePricePerPartRial = saleRial;
        if (lowCentralParts is { } low) row.LowCentralParts = Math.Max(0, low);
        row.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static string? Cut(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim()[..Math.Min(300, s.Trim().Length)];
}
