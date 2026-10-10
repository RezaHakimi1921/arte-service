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
    public const string CaseOpened = "case.opened";
    public const string CaseReady = "case.ready";
    public const string CaseDelivered = "case.delivered";
    /// <summary>Sent by hand from the case page when the customer asks for the link again.</summary>
    public const string CaseLink = "case.link";
    /// <summary>Satisfaction survey after delivery (link to the tracking page, /s/{code}).</summary>
    public const string SurveyRequest = "survey.request";
}
