using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arte.Api.Options;
using Arte.Core.Common;
using Arte.Core.Messaging;
using Microsoft.Extensions.Options;

namespace Arte.Api.Sms;

/// <summary>
/// Faraz SMS / Iran Payamak pattern API (POST https://api.iranpayamak.com/ws/v1/sms/pattern, header Api-Key):
/// an approved pattern code, the recipient, the pattern's variables and the sender line. Pattern codes come from
/// configuration (<c>Sms:IranPayamak:Templates:auth_otp</c>); token "code" fills the variable named in
/// <c>Sms:IranPayamak:ParamNames:code</c> (default "code").
/// </summary>
public sealed class IranPayamakProvider(HttpClient http, IOptions<SmsOptions> options, ILogger<IranPayamakProvider> logger) : ISmsProvider
{
    public string Name => "iranpayamak";

    private sealed record PatternRequest(
        [property: JsonPropertyName("code")] string Code,
        [property: JsonPropertyName("recipient")] string Recipient,
        [property: JsonPropertyName("attributes")] Dictionary<string, string> Attributes,
        [property: JsonPropertyName("line_number")] string LineNumber,
        [property: JsonPropertyName("number_format")] string NumberFormat);

    public async Task<SmsSendResult> SendTemplateAsync(string mobile, string messageKey,
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

            // The provider's message never contains the code or the key.
            logger.LogError("iranpayamak pattern failed for {Mobile}: HTTP {Http} {Status} {Message}",
                Mobile.Mask(mobile), (int)res.StatusCode, status, message is { Length: > 300 } ? message[..300] : message);
            return new SmsSendResult(false, null, message ?? $"http_{(int)res.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogError("iranpayamak unreachable for {Mobile}: {Error}", Mobile.Mask(mobile), ex.GetType().Name);
            return new SmsSendResult(false, null, "unreachable");
        }
    }
}
