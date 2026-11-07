using System.Text;
using System.Text.RegularExpressions;

namespace Arte.Core.Messaging;

/// <summary>Tags a message text may contain, written {{Tag}}.</summary>
public static class SmsTags
{
    public const string CustomerName = "CustomerName";
    public const string CustomerLink = "CustomerLink";
    public const string WorkshopName = "WorkshopName";
    public const string CaseCode = "CaseCode";
    public const string Vehicle = "Vehicle";
    public const string Code = "Code";

    public static readonly IReadOnlyDictionary<string, string> Titles = new Dictionary<string, string>
    {
        [CustomerName] = "نام مشتری",
        [CustomerLink] = "لینک پیگیری یا نظرسنجی مشتری",
        [WorkshopName] = "نام تعمیرگاه / شعبه",
        [CaseCode] = "کد پرونده (CASE-123)",
        [Vehicle] = "وسیله‌ی نقلیه",
        [Code] = "کد ورود (فقط پیامک ورود)",
    };

    /// <summary>Sample values for the preview.</summary>
    public static readonly IReadOnlyDictionary<string, string> Samples = new Dictionary<string, string>
    {
        [CustomerName] = "رضا احمدی",
        [CustomerLink] = "service.artepersia.com/t/abcd234xyz",
        [WorkshopName] = "مکانیک موتور",
        [CaseCode] = "CASE-123",
        [Vehicle] = "موتور هوندا ۱۲۵",
        [Code] = "123456",
    };

    private static readonly Regex Pattern = new(@"\{\{\s*([A-Za-z]+)\s*\}\}", RegexOptions.Compiled);

    public static IEnumerable<string> Used(string text) => Pattern.Matches(text).Select(m => m.Groups[1].Value).Distinct();

    public static string Fill(string text, IReadOnlyDictionary<string, string> values) =>
        Pattern.Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : "");
}

/// <summary>The messages a branch can word itself, with the system default text of each.</summary>
public static class SmsTemplates
{
    public const int MaxLength = 400;

    public sealed record Definition(string Key, string Title, string Default, bool NeedsLink);

    /// <summary>Per-branch messages. The login code (auth.otp) is system-wide and not listed.</summary>
    public static readonly IReadOnlyList<Definition> Branch =
    [
        new(MessageKeys.CaseOpened, "هنگام پذیرش",
            "آرته سرویس\nنام: {{CustomerName}}\nوسیله: {{Vehicle}}\nتعمیرگاه: {{WorkshopName}}\nوضعیت: پذیرش شد و در نوبت کار قرار گرفت.\nلینک پیگیری: {{CustomerLink}}", true),
        new(MessageKeys.CaseReady, "آماده‌ی تحویل",
            "آرته سرویس\nنام: {{CustomerName}}\nوسیله: {{Vehicle}}\nتعمیرگاه: {{WorkshopName}}\nوضعیت: آماده‌ی تحویل است.\nلینک جزئیات و هزینه: {{CustomerLink}}", true),
        new(MessageKeys.CaseDelivered, "هنگام تحویل",
            "آرته سرویس\nنام: {{CustomerName}}\nوسیله: {{Vehicle}}\nتعمیرگاه: {{WorkshopName}}\nوضعیت: تحویل شد.\nلینک سابقه و ضمانت: {{CustomerLink}}", true),
        new(MessageKeys.CaseLink, "ارسال دوباره‌ی لینک (از پرونده)",
            "آرته سرویس\nنام: {{CustomerName}}\nوسیله: {{Vehicle}}\nتعمیرگاه: {{WorkshopName}}\nلینک پیگیری: {{CustomerLink}}", true),
        new(MessageKeys.CaseWaitingPart, "منتظر قطعه‌ی مشتری",
            "آرته سرویس\nنام: {{CustomerName}}\nوسیله: {{Vehicle}}\nتعمیرگاه: {{WorkshopName}}\nوضعیت: منتظر قطعه‌ای هستیم که شما تهیه می‌کنید.\nلینک پیگیری: {{CustomerLink}}", true),
        new(MessageKeys.SurveyRequest, "نظرسنجی بعد از تحویل",
            "آرته سرویس\nنام: {{CustomerName}}\nتعمیرگاه: {{WorkshopName}}\nلطفاً نظر خود را درباره‌ی این تعمیر ثبت کنید.\nلینک نظرسنجی: {{CustomerLink}}", true),
        new(MessageKeys.StaffWorkFinished, "به استادکار: کار در انتظار بازبینی",
            "آرته سرویس\nتعمیرگاه: {{WorkshopName}}\nکار {{CaseCode}} ({{Vehicle}}) تمام شد و منتظر بازبینی شماست.", false),
    ];

    public const string OtpDefault = "آرته سرویس\nکد ورود: {{Code}}\nاین کد را به کسی ندهید.";

    public static Definition? Find(string key) => Branch.FirstOrDefault(d => d.Key == key);

    public static string DefaultFor(string key) =>
        key == MessageKeys.AuthOtp ? OtpDefault : Find(key)?.Default ?? "";

    /// <summary>Persian error for a text the admin cannot save, or null when it is fine.</summary>
    public static string? Validate(string key, string text)
    {
        var def = Find(key);
        if (def is null) return "این نوع پیام قابل تنظیم نیست.";
        if (string.IsNullOrWhiteSpace(text)) return "متن پیامک خالی است.";
        if (text.Length > MaxLength) return $"متن پیامک از {MaxLength} نویسه بلندتر است.";
        var unknown = SmsTags.Used(text).Where(t => !SmsTags.Titles.ContainsKey(t) || t == SmsTags.Code).ToList();
        if (unknown.Count > 0) return $"تگ ناشناخته: {string.Join("، ", unknown.Select(t => "{{" + t + "}}"))}";
        if (def.NeedsLink && !SmsTags.Used(text).Contains(SmsTags.CustomerLink)) return "این پیام باید {{CustomerLink}} را داشته باشد.";
        return null;
    }
}

/// <summary>
/// How many billable parts a text takes. An estimate from the standard rules (GSM-7: 160 / 153 per part, anything
/// else, Persian included, is UCS-2: 70 / 67); the operator line has the last word.
/// </summary>
public static class SmsParts
{
    private const string Gsm7Basic =
        "@£$¥èéùìòÇ\nØø\rÅåΔ_ΦΓΛΩΠΨΣΘΞÆæßÉ !\"#¤%&'()*+,-./0123456789:;<=>?¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§¿abcdefghijklmnopqrstuvwxyzäöñüà";
    private const string Gsm7Extended = "^{}\\[~]|€\f";

    public static int Count(string body)
    {
        if (string.IsNullOrEmpty(body)) return 0;
        var gsmUnits = 0;
        var gsm = true;
        foreach (var ch in body)
        {
            if (Gsm7Basic.Contains(ch)) gsmUnits++;
            else if (Gsm7Extended.Contains(ch)) gsmUnits += 2;
            else { gsm = false; break; }
        }
        if (gsm) return gsmUnits <= 160 ? 1 : (int)Math.Ceiling(gsmUnits / 153.0);
        var units = Encoding.Unicode.GetByteCount(body) / 2;   // UTF-16 code units: an emoji counts 2
        return units <= 70 ? 1 : (int)Math.Ceiling(units / 67.0);
    }
}
