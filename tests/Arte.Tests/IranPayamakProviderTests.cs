using System.Net;
using System.Text;
using System.Text.Json;
using Arte.Api.Options;
using Arte.Api.Sms;
using Arte.Core.Messaging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arte.Tests;

/// <summary>The Faraz SMS / Iran Payamak adapter against a stub handler: no real message is ever sent from tests.</summary>
public sealed class IranPayamakProviderTests
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

    private static (IranPayamakProvider Provider, Stub Stub) Make(HttpStatusCode status, string body, bool withPattern = true)
    {
        var stub = new Stub(status, body);
        var http = new HttpClient(stub) { BaseAddress = new Uri("https://api.iranpayamak.com/") };
        var o = new SmsOptions { Provider = "iranpayamak", IranPayamak = new IranPayamakOptions { ApiKey = "test-key-not-real", LineNumber = "3000505" } };
        if (withPattern) o.IranPayamak.Templates["auth_otp"] = "XBk5FjbVlz";
        o.IranPayamak.ParamNames["code"] = "otp";
        return (new IranPayamakProvider(http, Microsoft.Extensions.Options.Options.Create(o), NullLogger<IranPayamakProvider>.Instance), stub);
    }

    private static readonly Dictionary<string, string> Code = new() { ["code"] = "48213" };

    [Fact]
    public async Task Sends_the_pattern_with_its_variable_and_line()
    {
        var (sms, stub) = Make(HttpStatusCode.Created, """{"status":"success","data":123456,"messages":null}""");
        var result = await sms.SendTemplateAsync("09121234567", MessageKeys.AuthOtp, Code, CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("123456", result.ProviderMessageId);
        Assert.Equal("https://api.iranpayamak.com/ws/v1/sms/pattern", stub.Request!.RequestUri!.ToString());
        var json = JsonDocument.Parse(stub.RequestBody!).RootElement;
        Assert.Equal("XBk5FjbVlz", json.GetProperty("code").GetString());
        Assert.Equal("09121234567", json.GetProperty("recipient").GetString());
        Assert.Equal("3000505", json.GetProperty("line_number").GetString());
        Assert.Equal("english", json.GetProperty("number_format").GetString());
        Assert.Equal("48213", json.GetProperty("attributes").GetProperty("otp").GetString());
    }

    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, """{"status":"error","data":null,"messages":{"code":["الگو یافت نشد"]}}""")]
    [InlineData(HttpStatusCode.Unauthorized, """{"status":"error","message":"Auth required","data":null}""")]
    [InlineData(HttpStatusCode.BadGateway, "<html>bad gateway</html>")]
    public async Task Provider_failure_is_reported_not_thrown(HttpStatusCode status, string body)
    {
        var (sms, _) = Make(status, body);
        var result = await sms.SendTemplateAsync("09121234567", MessageKeys.AuthOtp, Code, CancellationToken.None);
        Assert.False(result.Accepted);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task Missing_pattern_does_not_call_the_provider()
    {
        var (sms, stub) = Make(HttpStatusCode.Created, "{}", withPattern: false);
        var result = await sms.SendTemplateAsync("09121234567", MessageKeys.AuthOtp, Code, CancellationToken.None);
        Assert.False(result.Accepted);
        Assert.Null(stub.Request);
    }
}
