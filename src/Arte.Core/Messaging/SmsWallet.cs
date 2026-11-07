namespace Arte.Core.Messaging;

/// <summary>
/// Platform-wide SMS settings and the central (head-office) balance. One row, Id = 1. Not owned by a business.
/// The central balance mirrors what Arte holds at SmartX, counted in message parts; every part a business or Arte
/// itself sends lowers it, so the sum of the branch balances can never be more than it (see SmsWalletService).
/// </summary>
public sealed class SmsPlatformSettings
{
    public int Id { get; set; } = 1;
    /// <summary>What Arte pays SmartX for one part (rials). 100 toman = 1,000 rials.</summary>
    public long PurchasePricePerPartRial { get; set; } = 1_000;
    /// <summary>What a branch pays Arte for one part (rials). 219 toman = 2,190 rials.</summary>
    public long SalePricePerPartRial { get; set; } = 2_190;
    public long CentralBalanceParts { get; set; }
    /// <summary>Below this the admin panel shows a «شارژ دفتر مرکزی» warning.</summary>
    public long LowCentralParts { get; set; } = 50;
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>A branch's SMS credit in parts. Platform-managed, so not tenant-filtered; every query names its TenantId.</summary>
public sealed class TenantSmsWallet
{
    public Guid TenantId { get; set; }
    public long BalanceParts { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class SmsLedgerKinds
{
    public const string CentralTopUp = "central_topup";   // Arte bought credit at SmartX
    public const string CentralSync = "central_sync";     // balance set to what SmartX shows
    public const string Credit = "credit";                // admin gave the branch credit (negative: took back)
    public const string Spend = "spend";                  // a branch's message went out
    public const string SpendArte = "spend_arte";         // a message Arte pays for (login code)
    public const string Refund = "refund";                // the message was not accepted: parts returned
}

/// <summary>Append-only record of every change to a balance.</summary>
public sealed class SmsLedgerEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>The branch whose balance changed; null: only the central balance.</summary>
    public Guid? TenantId { get; set; }
    public required string Kind { get; set; }
    /// <summary>Change of the balance (parts): negative for spending.</summary>
    public long Parts { get; set; }
    /// <summary>The branch's balance after this entry (null for central-only entries).</summary>
    public long? BalanceAfter { get; set; }
    public long CentralAfter { get; set; }
    /// <summary>What was paid (rials), when the entry came from an amount.</summary>
    public long? AmountRial { get; set; }
    public string? MessageKey { get; set; }
    public string? Note { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A branch's own text for one message. The draft is what the admin is editing; only the approved text is sent.
/// With no approved text the system default is used (unless the server requires approval).
/// </summary>
public sealed class SmsTemplate
{
    public Guid TenantId { get; set; }
    public required string MessageKey { get; set; }
    public string? DraftText { get; set; }
    public string? ApprovedText { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
