using System.Net;
using System.Text;
using System.Text.Json;
using Arte.Api.Options;
using Arte.Api.Sms;
using Arte.Core.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arte.Tests;

/// <summary>The sms.ir adapter against a stub HTTP handler: no real message is ever sent from tests.</summary>
public sealed class SmsIrProviderTests
{
    private sealed class Stub(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? RequestBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (SmsIrProvider Provider, Stub Stub) Make(HttpStatusCode status, string body, bool withTemplate = true)
    {
        var stub = new Stub(status, body);
        var http = new HttpClient(stub) { BaseAddress = new Uri("https://api.sms.ir/") };
        var options = new SmsOptions { Provider = "smsir", SmsIr = new SmsIrOptions { ApiKey = "test-key-not-real-000000" } };
        if (withTemplate) options.SmsIr.Templates["auth_otp"] = 123456;
        return (new SmsIrProvider(http, Microsoft.Extensions.Options.Options.Create(options), NullLogger<SmsIrProvider>.Instance), stub);
    }

    private static readonly Dictionary<string, string> Code = new() { ["code"] = "48213" };

    [Fact]
    public async Task Sends_the_template_with_named_parameters()
    {
        var (sms, stub) = Make(HttpStatusCode.OK, """{"status":1,"message":"موفق","data":{"messageId":987654,"cost":1.0}}""");
        var result = await sms.SendTemplateAsync("09121234567", MessageKeys.AuthOtp, Code, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("987654", result.ProviderMessageId);
        Assert.Equal(HttpMethod.Post, stub.Request!.Method);
        Assert.Equal("https://api.sms.ir/v1/send/verify", stub.Request.RequestUri!.ToString());
        var json = JsonDocument.Parse(stub.RequestBody!).RootElement;
        Assert.Equal("09121234567", json.GetProperty("mobile").GetString());
        Assert.Equal(123456, json.GetProperty("templateId").GetInt32());
        var p = json.GetProperty("parameters")[0];
        Assert.Equal("CODE", p.GetProperty("name").GetString());
        Assert.Equal("48213", p.GetProperty("value").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"status":0,"message":"قالب یافت نشد","data":null}""")]
    [InlineData(HttpStatusCode.Unauthorized, """{"status":0,"message":"کلید نامعتبر","data":null}""")]
    [InlineData(HttpStatusCode.BadGateway, "<html>bad gateway</html>")]
    public async Task Provider_failure_is_reported_not_thrown(HttpStatusCode status, string body)
    {
        var (sms, _) = Make(status, body);
        var result = await sms.SendTemplateAsync("09121234567", MessageKeys.AuthOtp, Code, CancellationToken.None);
        Assert.False(result.Accepted);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Missing_template_does_not_call_the_provider()
    {
        var (sms, stub) = Make(HttpStatusCode.OK, "{}", withTemplate: false);
        var result = await sms.SendTemplateAsync("09121234567", MessageKeys.AuthOtp, Code, CancellationToken.None);
        Assert.False(result.Accepted);
        Assert.Null(stub.Request);
    }
}
