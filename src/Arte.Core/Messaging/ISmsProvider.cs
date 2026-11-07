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

    /// <summary>
    /// The same send, told which business it is for (null: Arte itself, e.g. a login code) and extra tag values.
    /// Providers that know nothing about businesses keep the default.
    /// </summary>
    Task<SmsSendResult> SendAsync(SmsRequest request, CancellationToken ct) =>
        SendTemplateAsync(request.Mobile, request.MessageKey, request.Tokens, ct);
}

public sealed record SmsRequest(string Mobile, string MessageKey, IReadOnlyDictionary<string, string> Tokens,
    Guid? TenantId = null, IReadOnlyDictionary<string, string>? Extra = null);

public sealed record SmsSendResult(bool Accepted, string? ProviderMessageId, string? Error);

/// <summary>
/// Longest value of each pattern variable, as set on the provider's patterns (Faraz). Longer values are cut, so the
/// provider never refuses a message for length.
/// </summary>
public static class SmsLimits
{
    public const int Shop = 40, Name = 40, Vehicle = 40, Code = 12;
}

public static class MessageKeys
{
    public const string AuthOtp = "auth.otp";
    public const string CaseOpened = "case.opened";
    public const string CaseReady = "case.ready";
    public const string CaseDelivered = "case.delivered";
    /// <summary>Sent by hand from the case page when the customer asks for the link again.</summary>
    public const string CaseLink = "case.link";
    /// <summary>The case waits for a part the customer brings (setting: SmsOnWaitingPart).</summary>
    public const string CaseWaitingPart = "case.waiting_part";
    /// <summary>To the master: someone finished a job and it waits for his review (setting: SmsToMasterOnFinished).</summary>
    public const string StaffWorkFinished = "staff.work_finished";
    /// <summary>Satisfaction survey after delivery (link to the tracking page, /s/{code}).</summary>
    public const string SurveyRequest = "survey.request";
}
