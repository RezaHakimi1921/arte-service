using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Arte.Core.Common;
using Arte.Core.Messaging;
using Arte.Api.Options;
using Microsoft.Extensions.Options;

namespace Arte.Api.Sms;

/// <summary>
/// sms.ir "verify" (ارسال سریع): a provider-approved template with named parameters, sent from the shared service line.
/// Template ids come from configuration (<c>Sms:SmsIr:Templates:auth_otp</c>); a token "code" fills the template
/// parameter "CODE" unless <c>Sms:SmsIr:ParamNames</c> maps it otherwise.
/// </summary>
public sealed class SmsIrProvider(HttpClient http, IOptions<SmsOptions> options, ILogger<SmsIrProvider> logger) : ISmsProvider
{
    public const string HttpClientName = "smsir";
    public string Name => "smsir";

    private sealed record Param([property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("value")] string Value);
    private sealed record VerifyRequest(
        [property: JsonPropertyName("mobile")] string Mobile,
        [property: JsonPropertyName("templateId")] int TemplateId,
        [property: JsonPropertyName("parameters")] Param[] Parameters);
    private sealed record VerifyData([property: JsonPropertyName("messageId")] long? MessageId);
    private sealed record VerifyResponse(
        [property: JsonPropertyName("status")] int Status,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("data")] VerifyData? Data);

    public async Task<SmsSendResult> SendTemplateAsync(string mobile, string messageKey,
        IReadOnlyDictionary<string, string> tokens, CancellationToken ct)
    {
        var o = options.Value.SmsIr;
        if (!o.Templates.TryGetValue(messageKey.Replace('.', '_'), out var templateId))
        {
            logger.LogError("sms.ir: no template configured for {MessageKey}", messageKey);
            return new SmsSendResult(false, null, "template_not_configured");
        }

        var body = new VerifyRequest(mobile, templateId,
            tokens.Select(t => new Param(o.ParamNames.GetValueOrDefault(t.Key, t.Key.ToUpperInvariant()), t.Value)).ToArray());
        try
        {
            using var res = await http.PostAsJsonAsync("v1/send/verify", body, ct);
            var parsed = await res.Content.ReadFromJsonAsync<VerifyResponse>(ct);
            if (res.IsSuccessStatusCode && parsed?.Status == 1)
                return new SmsSendResult(true, parsed.Data?.MessageId?.ToString(), null);

            // The provider's message never contains the code or the key, so it is safe to log.
            logger.LogError("sms.ir verify failed for {Mobile}: HTTP {Http} status {Status} {Message}",
                Mobile.Mask(mobile), (int)res.StatusCode, parsed?.Status, parsed?.Message);
            return new SmsSendResult(false, null, parsed?.Message ?? $"http_{(int)res.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            logger.LogError("sms.ir unreachable for {Mobile}: {Error}", Mobile.Mask(mobile), ex.GetType().Name);
            return new SmsSendResult(false, null, "unreachable");
        }
    }
}
