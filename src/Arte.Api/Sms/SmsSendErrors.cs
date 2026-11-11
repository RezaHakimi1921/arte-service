namespace Arte.Api.Sms;

/// <summary>Stable error codes returned by shop SMS providers (Faraz free-text or SmartX when re-enabled).</summary>
public static class SmsSendErrors
{
    public const string NotApproved = "template_not_approved";
    public const string OutsideHours = "outside_sending_hours";
    public const string SendFailed = "provider_not_accepted";
    public const string SkippedNoCredit = "skipped_no_credit";
    public const string SkippedLicense = "skipped_license";
}
