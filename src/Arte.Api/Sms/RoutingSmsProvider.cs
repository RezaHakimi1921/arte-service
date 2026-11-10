using Arte.Core.Messaging;

namespace Arte.Api.Sms;

/// <summary>
/// Decides, per message, which provider sends it:
///  • the login code (auth.otp) ALWAYS goes to the legacy provider (Faraz / IranPayamak), never to SmartX;
///  • with <c>Sms:SmartXAll=true</c> every other message goes to SmartX;
///  • otherwise only the keys listed in <c>Sms:SmartXKeys</c> go to SmartX and the rest stay on the legacy provider.
/// </summary>
public sealed class RoutingSmsProvider(ISmsProvider legacy, ISmsProvider smartx, IReadOnlySet<string> smartxKeys, bool smartxAll) : ISmsProvider
{
    public string Name => $"{legacy.Name}+{smartx.Name}";

    public Task<SmsSendResult> SendTemplateAsync(string mobile, string messageKey, IReadOnlyDictionary<string, string> tokens, CancellationToken ct) =>
        SendAsync(new SmsRequest(mobile, messageKey, tokens), ct);

    public Task<SmsSendResult> SendAsync(SmsRequest request, CancellationToken ct) =>
        ToSmartX(request.MessageKey) ? smartx.SendAsync(request, ct) : legacy.SendAsync(request, ct);

    public bool ToSmartX(string messageKey) =>
        messageKey != Arte.Core.Messaging.MessageKeys.AuthOtp && (smartxAll || smartxKeys.Contains(messageKey));
}
