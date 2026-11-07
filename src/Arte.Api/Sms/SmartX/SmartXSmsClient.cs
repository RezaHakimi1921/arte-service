using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arte.Core.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Arte.Api.Sms.SmartX;

public sealed class SmartXSmsClient
{
    public const string HttpClientName = "smartx";
    public const int HardMaxRecipients = 100;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IHttpClientFactory _factory;
    private readonly SmartXTokenProvider _tokens;
    private readonly ISmsBatchLog _log;
    private readonly SmartXSmsOptions _o;
    private readonly ILogger<SmartXSmsClient> _logger;

    public SmartXSmsClient(IHttpClientFactory factory, SmartXTokenProvider tokens, ISmsBatchLog log,
        IOptions<SmartXSmsOptions> options, ILogger<SmartXSmsClient> logger)
    {
        _factory = factory;
        _tokens = tokens;
        _log = log;
        _o = options.Value;
        _logger = logger;
    }

    /// لیست را به بچ‌های حداکثر ۱۰۰ تایی می‌شکند؛ برای هر بچ یک UUID تازه می‌سازد، آن را قبل از ارسال ذخیره
    /// می‌کند و می‌فرستد. روی timeout/5xx همان UUID دوباره فرستاده می‌شود.
    /// شماره یا متن نامعتبر ارسال نمی‌شود و در Invalid برمی‌گردد.
    public async Task<SendResult> SendAsync(IEnumerable<SmsRecipient> recipients, SmsSendOptions? options = null, CancellationToken ct = default)
    {
        _o.Validate();

        var valid = new List<SmsRecipient>();
        var invalid = new List<SmsRecipient>();
        foreach (var r in recipients)
        {
            // Mobile.TryNormalize gives 09121234567; SmartX documents the 98912… form.
            if (Mobile.TryNormalize(r.Mobile, out var local) && !string.IsNullOrWhiteSpace(r.Body))
                valid.Add(r with { Mobile = "98" + local[1..] });
            else
                invalid.Add(r);
        }

        var size = Math.Clamp(_o.MaxRecipientsPerRequest, 1, HardMaxRecipients);
        var chunks = valid.Chunk(size)
            .Select(c => (BatchId: Guid.NewGuid(), Recipients: (IReadOnlyList<SmsRecipient>)c))
            .ToList();

        // ۱) UUID همهٔ بچ‌ها قبل از اولین تماس شبکه‌ای ذخیره می‌شود؛ چیزی گم نمی‌شود.
        foreach (var c in chunks)
            await _log.BeginAsync(c.BatchId, c.Recipients, ct);

        // ۲) ارسال
        var results = new List<SendBatchResult>(chunks.Count);
        foreach (var c in chunks)
            results.Add(await SendBatchAsync(c.BatchId, c.Recipients, options, ct));

        return new SendResult(results, invalid);
    }

    /// بچ‌های نیمه‌کاره (Pending) را با همان UUID دوباره می‌فرستد. هر چند دقیقه یک بار صدا بزن.
    public async Task<IReadOnlyList<SendBatchResult>> ResumePendingAsync(int max = 50, CancellationToken ct = default)
    {
        _o.Validate();
        var results = new List<SendBatchResult>();
        foreach (var p in await _log.ClaimPendingAsync(max, ct))
            results.Add(await SendBatchAsync(p.BatchId, p.Recipients, null, ct));
        return results;
    }

    private async Task<SendBatchResult> SendBatchAsync(Guid batchId, IReadOnlyList<SmsRecipient> recipients, SmsSendOptions? options, CancellationToken ct)
    {
        var body = BuildRequest(batchId, recipients, options);   // برای همهٔ تلاش‌ها یکی است (همان UUID)
        var http = _factory.CreateClient(HttpClientName);
        var maxAttempts = Math.Max(1, _o.MaxAttempts);

        int attempt = 0;
        int? status = null;
        string? error = null;
        bool permanent = false, refreshed = false;
        string? rejectedToken = null;

        while (attempt < maxAttempts)
        {
            attempt++;
            TimeSpan? retryAfter = null;
            try
            {
                var token = await _tokens.GetTokenAsync(ct, rejectedToken);
                using var req = new HttpRequestMessage(HttpMethod.Post, "/api/v1/notification/direct-send")
                {
                    Content = JsonContent.Create(body, options: Json),
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                using var resp = await http.SendAsync(req, ct);
                status = (int)resp.StatusCode;

                if (resp.IsSuccessStatusCode)
                {
                    await TryUpdateLogAsync(batchId, SmsBatchState.Accepted, status, null);
                    _logger.LogInformation("SmartX batch {BatchId} accepted ({Count} recipients, attempt {Attempt})",
                        batchId, recipients.Count, attempt);
                    return new SendBatchResult(batchId, recipients.Count, SmsBatchState.Accepted, status, null, attempt);
                }

                error = $"HTTP {status}: {Truncate(await resp.Content.ReadAsStringAsync(ct))}";

                // توکن رد شد: یک بار توکن تازه بگیر و بلافاصله دوباره بفرست (جزو تلاش‌ها حساب نمی‌شود)
                if (resp.StatusCode == HttpStatusCode.Unauthorized && !refreshed)
                {
                    refreshed = true;
                    rejectedToken = token;
                    attempt--;
                    continue;
                }

                if (!IsRetryable(resp.StatusCode))
                {
                    permanent = true;
                    break;
                }
                retryAfter = resp.Headers.RetryAfter?.Delta;
            }
            catch (HttpRequestException ex)
            {
                status = ex.StatusCode.HasValue ? (int)ex.StatusCode.Value : null;
                error = $"{ex.GetType().Name}: {Truncate(ex.Message)}";
                if (ex.StatusCode is { } code && !IsRetryable(code))
                {
                    permanent = true;
                    break;
                }
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                status = null;
                error = "Request timed out";
            }

            if (attempt < maxAttempts)
            {
                _logger.LogWarning("SmartX batch {BatchId} attempt {Attempt}/{Max} failed ({Error}); retrying with the same UUID",
                    batchId, attempt, maxAttempts, error);
                await Task.Delay(Backoff(attempt, retryAfter), ct);
            }
        }

        // permanent → Failed (دیگر تلاش نمی‌شود)؛ retry تمام شد → Pending (قابل ResumePendingAsync)
        var state = permanent ? SmsBatchState.Failed : SmsBatchState.Pending;
        await TryUpdateLogAsync(batchId, state, status, error);
        _logger.LogError("SmartX batch {BatchId} not accepted: {State}, {Error}", batchId, state, error);
        return new SendBatchResult(batchId, recipients.Count, state, status, error, attempt);
    }

    private async Task TryUpdateLogAsync(Guid batchId, SmsBatchState state, int? status, string? error)
    {
        try
        {
            await _log.UpdateAsync(batchId, state, status, error, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not update SmartX batch log for {BatchId} (state {State})", batchId, state);
        }
    }

    private DirectSendRequest BuildRequest(Guid batchId, IReadOnlyList<SmsRecipient> recipients, SmsSendOptions? options) => new()
    {
        CorrelationId = batchId.ToString(),
        ChannelName = "Sms",
        SenderNumber = _o.SenderNumber,
        RecipientsTypeName = _o.RecipientsTypeName,
        Locale = _o.Locale,
        ShouldCheckQuota = _o.ShouldCheckQuota,
        ShouldCheckConsent = _o.ShouldCheckConsent,
        ShouldCheckQuietHours = options?.CheckQuietHours ?? _o.ShouldCheckQuietHours,
        RecipientInfo = recipients.Select(r => new RecipientDto
        {
            ContactInfo = r.Mobile,
            Body = r.Body,
            RecipientId = r.RecipientId,
            CorrelationId = r.CorrelationId,
        }).ToList(),
    };

    private static bool IsRetryable(HttpStatusCode c) =>
        c == HttpStatusCode.RequestTimeout || (int)c == 429 || (int)c >= 500;

    private TimeSpan Backoff(int attempt, TimeSpan? retryAfter)
    {
        var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Max(1, _o.RetryBaseDelaySeconds) * Math.Pow(2, attempt - 1)));
        if (retryAfter is { } ra && ra > TimeSpan.Zero) delay = ra < TimeSpan.FromSeconds(60) ? ra : TimeSpan.FromSeconds(60);
        return delay;
    }

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500];

    // ---- بدنهٔ JSON مطابق مستند Partner API ----
    private sealed class DirectSendRequest
    {
        public required string CorrelationId { get; init; }
        public required string ChannelName { get; init; }
        public required string SenderNumber { get; init; }
        public required string RecipientsTypeName { get; init; }
        public required string Locale { get; init; }
        public bool ShouldCheckQuota { get; init; }
        public bool ShouldCheckConsent { get; init; }
        public bool ShouldCheckQuietHours { get; init; }
        public required List<RecipientDto> RecipientInfo { get; init; }
    }

    private sealed class RecipientDto
    {
        public required string ContactInfo { get; init; }
        public required string Body { get; init; }
        public long RecipientId { get; init; }
        public string? CorrelationId { get; init; }
    }
}
