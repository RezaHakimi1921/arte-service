namespace Arte.Core.Messaging;

/// <summary>
/// Shop SMS that could not send because the branch had no credit. Flushed after the next successful top-up.
/// OTP login codes are never stored here. Not ITenantOwned: platform admin top-up flushes without a tenant scope.
/// </summary>
public sealed class SmsDeferredMessage
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string Mobile { get; set; }
    public required string MessageKey { get; set; }
    /// <summary>JSON object of template tokens.</summary>
    public required string TokensJson { get; set; }
    /// <summary>Optional extra JSON (e.g. CaseCode).</summary>
    public string? ExtraJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public int Attempts { get; set; }
    /// <summary>pending | sent | failed</summary>
    public string Status { get; set; } = SmsDeferredStatuses.Pending;
}

public static class SmsDeferredStatuses
{
    public const string Pending = "pending";
    public const string Sent = "sent";
    public const string Failed = "failed";
}
