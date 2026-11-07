using Arte.Api.Options;
using Arte.Core.Common;
using Arte.Core.Messaging;
using Microsoft.Extensions.Options;

namespace Arte.Api.Sms.SmartX;

/// <summary>
/// Sends through SmartX's direct-send. Per message: pick the text (the branch's approved one or the system's),
/// fill the tags, keep to Arte's sending hours, count the parts, take them from the right balance, send, and give
/// the parts back if SmartX did not accept the message. «Accepted» means SmartX took it, not that a phone got it.
/// </summary>
public sealed class SmartXSmsProvider(
    SmartXSmsClient client,
    SmsWalletService wallet,
    SmsTemplateService templates,
    IClock clock,
    IOptions<SmartXSmsOptions> options,
    ILogger<SmartXSmsProvider> logger) : ISmsProvider
{
    public const string NotApproved = "template_not_approved";
    public const string OutsideHours = "outside_sending_hours";
    public const string SendFailed = "smartx_not_accepted";

    private static readonly TimeSpan Tehran = TimeSpan.FromHours(3.5);   // Iran has no DST since 2022.

    public string Name => "smartx";

    public Task<SmsSendResult> SendTemplateAsync(string mobile, string messageKey, IReadOnlyDictionary<string, string> tokens, CancellationToken ct) =>
        SendAsync(new SmsRequest(mobile, messageKey, tokens), ct);

    public async Task<SmsSendResult> SendAsync(SmsRequest request, CancellationToken ct)
    {
        var o = options.Value;
        var isOtp = request.MessageKey == MessageKeys.AuthOtp;

        // 1) The text.
        var text = SmsTemplates.DefaultFor(request.MessageKey);
        if (!isOtp && request.TenantId is { } tid)
        {
            var approved = await templates.ApprovedTextAsync(tid, request.MessageKey, ct);
            if (approved is not null) text = approved;
            else if (o.RequireApprovedTemplate) return new SmsSendResult(false, null, NotApproved);
        }
        if (text.Length == 0) return new SmsSendResult(false, null, "unknown_message_key");
        var body = SmsTags.Fill(text, SmsTemplateService.Values(request.MessageKey, request.Tokens, request.Extra, o.LinkBase)).Trim();

        // 2) Arte's own window; the login code goes out at any hour.
        if (!isOtp && !InSendingHours(clock.UtcNow, o.SendFromHour, o.SendUntilHour))
            return new SmsSendResult(false, null, OutsideHours);

        // 3) Parts, from the branch (and the central balance), or from the central balance alone for Arte's own messages.
        var parts = SmsParts.Count(body);
        var payer = isOtp ? null : request.TenantId;
        var (reservation, error) = await wallet.ReserveAsync(payer, parts, request.MessageKey, ct);
        if (reservation is null) return new SmsSendResult(false, null, error);

        // 4) Send. Any failure hands the parts back.
        try
        {
            var result = await client.SendAsync(
                [new SmsRecipient(0, request.Mobile, body)],
                new SmsSendOptions(CheckQuietHours: isOtp ? o.OtpCheckQuietHours : null),
                ct);
            if (result.AllAccepted && result.Batches.Count == 1)
                return new SmsSendResult(true, result.Batches[0].BatchId.ToString("N"), null);

            await wallet.RefundAsync(reservation);
            logger.LogWarning("SmartX did not accept {MessageKey}: {Reason}", request.MessageKey,
                result.Batches.FirstOrDefault()?.Error ?? (result.Invalid.Count > 0 ? "invalid mobile or empty text" : "unknown"));
            return new SmsSendResult(false, null, SendFailed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await wallet.RefundAsync(reservation);
            logger.LogError(ex, "SmartX send of {MessageKey} failed", request.MessageKey);
            return new SmsSendResult(false, null, SendFailed);
        }
        catch (OperationCanceledException)
        {
            await wallet.RefundAsync(reservation);
            throw;
        }
    }

    public static bool InSendingHours(DateTimeOffset utc, int fromHour, int untilHour)
    {
        var hour = utc.ToOffset(Tehran).Hour;
        return hour >= fromHour && hour < untilHour;
    }
}
