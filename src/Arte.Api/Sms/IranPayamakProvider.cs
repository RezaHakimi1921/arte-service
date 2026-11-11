using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arte.Api.Licensing;
using Arte.Api.Options;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Messaging;
using Microsoft.Extensions.Options;

namespace Arte.Api.Sms;

/// <summary>
/// Faraz / Iran Payamak: OTP stays on the approved pattern API; every other shop message is free text
/// (<c>POST /ws/v1/sms/simple</c>) filled from the branch's approved Arte template (or the system default).
/// </summary>
public sealed class IranPayamakProvider(
    HttpClient http,
    IOptions<SmsOptions> options,
    SmsTemplateService templates,
    ArteDbContext db,
    IClock clock,
    ILogger<IranPayamakProvider> logger) : ISmsProvider
{
    private static readonly TimeSpan Tehran = TimeSpan.FromHours(3.5);

    public string Name => "iranpayamak";

    private sealed record PatternRequest(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("attributes")] Dictionary<string, string> Attributes,
        [property: JsonPropertyName("line_number")] string LineNumber,
        [property: JsonPropertyName("number_format")] string NumberFormat);

    private sealed record SimpleRequest(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("line_number")] string LineNumber,
        [property: JsonPropertyName("recipients")] string[] Recipients,
        [property: JsonPropertyName("number_format")] string NumberFormat,
        [property: JsonPropertyName("schedule")] string? Schedule);

    public Task<SmsSendResult> SendTemplateAsync(string mobile, string messageKey,
        IReadOnlyDictionary<string, string> tokens, CancellationToken ct) =>
        SendAsync(new SmsRequest(mobile, messageKey, tokens), ct);

    public async Task<SmsSendResult> SendAsync(SmsRequest request, CancellationToken ct)
    {
        if (request.MessageKey == MessageKeys.AuthOtp)
            return await SendPatternAsync(request.Mobile, request.MessageKey, request.Tokens, ct);

        var o = options.Value;
        if (request.TenantId is { } licenseTenant)
        {
            var access = await LicenseService.AccessAsync(db, licenseTenant, clock.UtcNow, ct);
            if (!access.CanSendSms) return new SmsSendResult(false, null, SmsSendErrors.SkippedLicense);
        }

        var text = SmsTemplates.DefaultFor(request.MessageKey);
        if (request.TenantId is { } tid)
        {
            var approved = await templates.ApprovedTextAsync(tid, request.MessageKey, ct);
            if (approved is not null) text = approved;
            else if (o.RequireApprovedTemplate) return new SmsSendResult(false, null, SmsSendErrors.NotApproved);
        }
        if (text.Length == 0) return new SmsSendResult(false, null, "unknown_message_key");

        if (!InSendingHours(clock.UtcNow, o.SendFromHour, o.SendUntilHour))
            return new SmsSendResult(false, null, SmsSendErrors.OutsideHours);

        var body = SmsTags.Fill(text, SmsTemplateService.Values(request.MessageKey, request.Tokens, request.Extra, o.LinkBase)).Trim();
        return await SendSimpleAsync(request.Mobile, body, ct);
    }

    private async Task<SmsSendResult> SendPatternAsync(string mobile, string messageKey,
        IReadOnlyDictionary<string, string> tokens, CancellationToken ct)
    {
        var o = options.Value.IranPayamak;
        if (!o.Templates.TryGetValue(messageKey.Replace('.', '_'), out var pattern) || string.IsNullOrWhiteSpace(pattern))
        {
            logger.LogError("iranpayamak: no pattern configured for {MessageKey}", messageKey);
            return new SmsSendResult(false, null, "template_not_configured");
        }

        var body = new PatternRequest(pattern, mobile,
            tokens.ToDictionary(t => o.ParamNames.GetValueOrDefault(t.Key, t.Key), t => t.Value), o.LineNumber, "english");
        try
        {
            using var res = await http.PostAsJsonAsync("ws/v1/sms/pattern", body, ct);
            return await ReadResultAsync(res, mobile, "pattern", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError("iranpayamak unreachable for {Mobile}: {Error}", Mobile.Mask(mobile), ex.GetType().Name);
            return new SmsSendResult(false, null, "unreachable");
        }
    }

    private async Task<SmsSendResult> SendSimpleAsync(string mobile, string text, CancellationToken ct)
    {
        var o = options.Value.IranPayamak;
        var body = new SimpleRequest(text, o.LineNumber, [mobile], "english", null);
        try
        {
            using var res = await http.PostAsJsonAsync("ws/v1/sms/simple", body, ct);
            return await ReadResultAsync(res, mobile, "simple", ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError("iranpayamak simple unreachable for {Mobile}: {Error}", Mobile.Mask(mobile), ex.GetType().Name);
            return new SmsSendResult(false, null, "unreachable");
        }
    }

    private async Task<SmsSendResult> ReadResultAsync(HttpResponseMessage res, string mobile, string kind, CancellationToken ct)
    {
        var text = await res.Content.ReadAsStringAsync(ct);
        string? status = null, id = null, message = null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            status = root.TryGetProperty("status", out var s) ? s.ToString() : null;
            id = root.TryGetProperty("data", out var d) && d.ValueKind is not JsonValueKind.Null ? d.ToString() : null;
            message = root.TryGetProperty("messages", out var m) && m.ValueKind is not JsonValueKind.Null ? m.ToString()
                : root.TryGetProperty("message", out var m2) ? m2.ToString() : null;
        }
        catch (JsonException) { }

        if (res.IsSuccessStatusCode && status == "success")
            return new SmsSendResult(true, id, null);

        logger.LogError("iranpayamak {Kind} failed for {Mobile}: HTTP {Http} {Status} {Message}",
            kind, Mobile.Mask(mobile), (int)res.StatusCode, status, message is { Length: > 300 } ? message[..300] : message);
        return new SmsSendResult(false, null, message ?? $"http_{(int)res.StatusCode}");
    }

    public static bool InSendingHours(DateTimeOffset utc, int fromHour, int untilHour)
    {
        var hour = utc.ToOffset(Tehran).Hour;
        return hour >= fromHour && hour < untilHour;
    }
}
