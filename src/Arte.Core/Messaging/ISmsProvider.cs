namespace Arte.Core.Messaging;

/// <summary>
/// Every SMS to a customer or staff member goes through a provider-approved template (pattern),
/// so switching providers is one adapter plus a MessageKey → TemplateId mapping in configuration.
/// </summary>
public interface ISmsProvider
{
    string Name { get; }

    Task<SmsSendResult> SendTemplateAsync(string mobile, string messageKey,
        IReadOnlyDictionary<string, string> tokens, CancellationToken ct);
}

public sealed record SmsSendResult(bool Accepted, string? ProviderMessageId, string? Error);

public static class MessageKeys
{
    public const string AuthOtp = "auth.otp";
}
