using Arte.Core.Messaging;

namespace Arte.Api.Sms;

/// <summary>
/// Moves messages to SmartX one key at a time while the old provider still carries the rest
/// (<c>Sms:SmartXKeys</c>). When every key is on SmartX, set <c>Sms:Provider=smartx</c> and this class is not used.
/// </summary>
public sealed class RoutingSmsProvider(ISmsProvider legacy, ISmsProvider smartx, IReadOnlySet<string> smartxKeys) : ISmsProvider
{
    public string Name => $"{legacy.Name}+{smartx.Name}";

    public Task<SmsSendResult> SendTemplateAsync(string mobile, string messageKey, IReadOnlyDictionary<string, string> tokens, CancellationToken ct) =>
        SendAsync(new SmsRequest(mobile, messageKey, tokens), ct);

    public Task<SmsSendResult> SendAsync(SmsRequest request, CancellationToken ct) =>
        smartxKeys.Contains(request.MessageKey)
            ? smartx.SendAsync(request, ct)
            : legacy.SendAsync(request, ct);
}
