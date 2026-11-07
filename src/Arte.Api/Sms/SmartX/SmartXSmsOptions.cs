namespace Arte.Api.Sms.SmartX;

/// تنظیمات از appsettings (بخش "SmartXSms") یا متغیر محیطی SmartXSms__<Name> خوانده می‌شود.
/// ClientSecret را فقط در env / secret store بگذار، نه در فایل و نه در چت.
public sealed class SmartXSmsOptions
{
    public const string Section = "SmartXSms";

    /// staging: https://staging-api.smartx.ir   —   production: https://api.smartx.ir
    public string BaseUrl { get; set; } = "https://staging-api.smartx.ir";

    public string ClientId { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string Scope { get; set; } = "api.full";

    /// خط ارسال؛ API آن را الزامی می‌کند. یک مقدار ثابت از config (بدون انتخاب پویا).
    public string SenderNumber { get; set; } = "";

    /// کنترل‌های سمت اسمارت (quota / consent / quiet hours / لغو ۱۱)
    public bool ShouldCheckQuota { get; set; } = true;
    public bool ShouldCheckConsent { get; set; } = true;
    public bool ShouldCheckQuietHours { get; set; } = true;

    public string RecipientsTypeName { get; set; } = "Customer";
    public string Locale { get; set; } = "Fa";

    /// سقف گیرنده در هر درخواست؛ هیچ‌وقت بیشتر از ۱۰۰ فرستاده نمی‌شود.
    /// <summary>Host (and path) of the customer pages; {{CustomerLink}} becomes LinkBase + "/t/" or "/s/" + code.</summary>
    public string LinkBase { get; set; } = "service.artepersia.com";
    /// <summary>
    /// Ask SmartX to hold the login code back during its quiet hours. Off: a code the user just asked for must arrive
    /// at any hour. Turn on only if SmartX requires it.
    /// </summary>
    public bool OtpCheckQuietHours { get; set; }
    /// <summary>Arte's own window for every message except the login code (Tehran time, hours).</summary>
    public int SendFromHour { get; set; } = 8;
    public int SendUntilHour { get; set; } = 23;
    /// <summary>On: a branch's message is sent only with the text the admin approved; off: the system text is used until then.</summary>
    public bool RequireApprovedTemplate { get; set; }

    public int MaxRecipientsPerRequest { get; set; } = 100;

    /// تعداد کل تلاش برای هر بچ (تلاش اول + retryها)، با همان UUID
    public int MaxAttempts { get; set; } = 4;
    public int RetryBaseDelaySeconds { get; set; } = 2;
    public int RequestTimeoutSeconds { get; set; } = 30;

    public void Validate()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(BaseUrl)) missing.Add(nameof(BaseUrl));
        if (string.IsNullOrWhiteSpace(ClientId)) missing.Add(nameof(ClientId));
        if (string.IsNullOrWhiteSpace(ClientSecret)) missing.Add(nameof(ClientSecret));
        if (string.IsNullOrWhiteSpace(TenantId)) missing.Add(nameof(TenantId));
        if (string.IsNullOrWhiteSpace(SenderNumber)) missing.Add(nameof(SenderNumber));
        if (missing.Count > 0)
            throw new InvalidOperationException(
                "SmartXSms config is missing: " + string.Join(", ", missing.Select(m => $"{Section}__{m}")));

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException($"{Section}__BaseUrl must be an absolute https URL.");
    }
}
