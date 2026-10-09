using System.Collections.Concurrent;
using Arte.Core.Common;
using Arte.Core.Messaging;

namespace Arte.Api.Sms;

/// <summary>Development stand-in: writes the message to the log instead of sending it.</summary>
public sealed class FakeSmsProvider(ILogger<FakeSmsProvider> logger) : ISmsProvider
{
    public string Name => "fake";

    /// <summary>Last message per mobile, read by tests.</summary>
    public ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Sent { get; } = new();

    /// <summary>Every message key sent per mobile, in order, read by tests.</summary>
    public ConcurrentDictionary<string, ConcurrentQueue<string>> Keys { get; } = new();

    public Task<SmsSendResult> SendTemplateAsync(string mobile, string messageKey,
        IReadOnlyDictionary<string, string> tokens, CancellationToken ct)
    {
        Sent[mobile] = tokens;
        Keys.GetOrAdd(mobile, _ => new ConcurrentQueue<string>()).Enqueue(messageKey);
        logger.LogWarning("FAKE SMS {MessageKey} to {Mobile}: {Tokens}",
            messageKey, Mobile.Mask(mobile), string.Join(", ", tokens.Select(t => $"{t.Key}={t.Value}")));
        return Task.FromResult(new SmsSendResult(true, Guid.NewGuid().ToString("N"), null));
    }
}
