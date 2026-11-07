using System.Net;
using System.Text;
using System.Text.Json;
using Arte.Api.Sms.SmartX;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Arte.Tests.SmartX;

public class SmartXSmsClientTests
{
    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(handler, disposeHandler: false) { BaseAddress = new Uri("https://staging-api.smartx.ir") };
    }

    /// تماس‌ها را ثبت می‌کند. responder برای direct-send (شمارهٔ تماس از ۱) پاسخ می‌دهد.
    private sealed class FakeHandler(Func<int, HttpResponseMessage> directSend) : HttpMessageHandler
    {
        public int TokenCalls;
        public readonly List<string> SendBodies = new();
        public readonly List<string?> SendTokens = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (req.RequestUri!.AbsolutePath == "/connect/token")
            {
                var n = Interlocked.Increment(ref TokenCalls);
                return Json(HttpStatusCode.OK, $"{{\"access_token\":\"tok{n}\",\"expires_in\":3600}}");
            }

            SendBodies.Add(await req.Content!.ReadAsStringAsync(ct));
            SendTokens.Add(req.Headers.Authorization?.Parameter);
            return directSend(SendBodies.Count);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (SmartXSmsClient Client, InMemorySmsBatchLog Log) Create(FakeHandler handler, Action<SmartXSmsOptions>? tweak = null)
    {
        var o = new SmartXSmsOptions
        {
            ClientId = "id", ClientSecret = "secret", TenantId = "42259", SenderNumber = "989982008609",
            RetryBaseDelaySeconds = 1, MaxAttempts = 4,
        };
        tweak?.Invoke(o);
        var factory = new FakeFactory(handler);
        var opts = Options.Create(o);
        var log = new InMemorySmsBatchLog();
        var client = new SmartXSmsClient(factory, new SmartXTokenProvider(factory, opts), log, opts, NullLogger<SmartXSmsClient>.Instance);
        return (client, log);
    }

    private static SmsRecipient[] People(int n) =>
        Enumerable.Range(1, n).Select(i => new SmsRecipient(i, $"0912000{i:0000}", $"سلام {i}")).ToArray();

    private static string CorrelationId(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("correlationId").GetString()!;

    [Fact]
    public async Task Retry_on_5xx_reuses_the_same_uuid()
    {
        var h = new FakeHandler(n => n < 3 ? Json(HttpStatusCode.ServiceUnavailable, "down") : new HttpResponseMessage(HttpStatusCode.OK));
        var (client, log) = Create(h, o => o.RetryBaseDelaySeconds = 1);

        var res = await client.SendAsync(People(1));

        Assert.True(res.AllAccepted);
        Assert.Equal(3, res.Batches[0].Attempts);
        Assert.Equal(3, h.SendBodies.Count);
        Assert.Single(h.SendBodies.Select(CorrelationId).Distinct());
        Assert.Equal(res.Batches[0].BatchId.ToString(), CorrelationId(h.SendBodies[0]));
        Assert.Equal(SmsBatchState.Accepted, log.Get(res.Batches[0].BatchId)!.Value.State);
    }

    [Fact]
    public async Task Splits_into_batches_of_at_most_100_with_distinct_uuids()
    {
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var (client, _) = Create(h, o => o.MaxRecipientsPerRequest = 5000);   // سقف سخت ۱۰۰ است

        var res = await client.SendAsync(People(250));

        Assert.Equal(new[] { 100, 100, 50 }, res.Batches.Select(b => b.RecipientCount).ToArray());
        Assert.Equal(3, h.SendBodies.Count);
        Assert.Equal(3, h.SendBodies.Select(CorrelationId).Distinct().Count());
        foreach (var body in h.SendBodies)
            Assert.True(JsonDocument.Parse(body).RootElement.GetProperty("recipientInfo").GetArrayLength() <= 100);
    }

    [Fact]
    public async Task Bad_request_is_not_retried_and_marked_failed()
    {
        var h = new FakeHandler(_ => Json(HttpStatusCode.BadRequest, "{\"error\":\"bad\"}"));
        var (client, log) = Create(h);

        var res = await client.SendAsync(People(1));

        Assert.Single(h.SendBodies);
        Assert.Equal(SmsBatchState.Failed, res.Batches[0].State);
        Assert.Equal(400, res.Batches[0].HttpStatus);
        Assert.Equal(SmsBatchState.Failed, log.Get(res.Batches[0].BatchId)!.Value.State);
    }

    [Fact]
    public async Task Unauthorized_refreshes_token_once_and_resends_same_uuid()
    {
        var h = new FakeHandler(n => n == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : new HttpResponseMessage(HttpStatusCode.OK));
        var (client, _) = Create(h);

        var res = await client.SendAsync(People(1));

        Assert.True(res.AllAccepted);
        Assert.Equal(2, h.TokenCalls);
        Assert.Equal(new[] { "tok1", "tok2" }, h.SendTokens);
        Assert.Single(h.SendBodies.Select(CorrelationId).Distinct());
    }

    [Fact]
    public async Task Exhausted_retries_leave_batch_pending_and_resume_uses_same_uuid()
    {
        var failing = true;
        var h = new FakeHandler(_ => failing ? Json(HttpStatusCode.BadGateway, "x") : new HttpResponseMessage(HttpStatusCode.OK));
        var (client, log) = Create(h, o => { o.MaxAttempts = 2; o.RetryBaseDelaySeconds = 1; });

        var res = await client.SendAsync(People(2));
        var id = res.Batches[0].BatchId;
        Assert.Equal(SmsBatchState.Pending, res.Batches[0].State);
        Assert.Equal(SmsBatchState.Pending, log.Get(id)!.Value.State);

        failing = false;
        var resumed = await client.ResumePendingAsync();

        Assert.Single(resumed);
        Assert.True(resumed[0].Accepted);
        Assert.Equal(id.ToString(), CorrelationId(h.SendBodies.Last()));
    }

    [Fact]
    public async Task Invalid_numbers_and_empty_bodies_are_skipped_and_numbers_normalized()
    {
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var (client, _) = Create(h);

        var res = await client.SendAsync(new[]
        {
            new SmsRecipient(1, "۰۹۱۲۱۲۳۴۵۶۷", "سلام"),
            new SmsRecipient(2, "12345", "سلام"),
            new SmsRecipient(3, "09121234567", "  "),
        });

        Assert.Equal(2, res.Invalid.Count);
        var info = JsonDocument.Parse(h.SendBodies[0]).RootElement.GetProperty("recipientInfo");
        Assert.Equal(1, info.GetArrayLength());
        Assert.Equal("989121234567", info[0].GetProperty("contactInfo").GetString());
        Assert.Equal("سلام", info[0].GetProperty("body").GetString());
    }

    [Fact]
    public async Task Request_matches_partner_api_contract()
    {
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var (client, _) = Create(h);

        await client.SendAsync(new[] { new SmsRecipient(1001, "09121234567", "متن", "order-7788") });

        var root = JsonDocument.Parse(h.SendBodies[0]).RootElement;
        Assert.Equal("Sms", root.GetProperty("channelName").GetString());
        Assert.Equal("989982008609", root.GetProperty("senderNumber").GetString());
        Assert.Equal("Customer", root.GetProperty("recipientsTypeName").GetString());
        Assert.Equal("Fa", root.GetProperty("locale").GetString());
        Assert.True(root.GetProperty("shouldCheckQuota").GetBoolean());
        Assert.True(root.GetProperty("shouldCheckConsent").GetBoolean());
        Assert.True(root.GetProperty("shouldCheckQuietHours").GetBoolean());
        var r = root.GetProperty("recipientInfo")[0];
        Assert.Equal(1001, r.GetProperty("recipientId").GetInt64());
        Assert.Equal("order-7788", r.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Missing_config_throws_without_calling_network()
    {
        var h = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var (client, _) = Create(h, o => o.ClientSecret = "");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync(People(1)));

        Assert.Contains("SmartXSms__ClientSecret", ex.Message);
        Assert.Empty(h.SendBodies);
    }
}
