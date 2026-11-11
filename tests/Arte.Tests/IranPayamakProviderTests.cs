using System.Net;
using System.Text;
using System.Text.Json;
using Arte.Api.Options;
using Arte.Api.Sms;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Messaging;
using Arte.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

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

    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }
    private sealed class NoTenant : ITenantContext { public Guid? TenantId => null; }

    // Daytime in Tehran (UTC+3:30) so shop SMS is inside 08–23.
    private static readonly DateTimeOffset Daytime = new(2026, 6, 1, 10, 0, 0, TimeSpan.FromHours(3.5));

    private static ArteDbContext UnusedDb() =>
        new(new DbContextOptionsBuilder<ArteDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=arte_unused;Username=x;Password=x").Options, new NoTenant());

    private static (IranPayamakProvider Provider, Stub Stub) Make(HttpStatusCode status, string body, bool withOtpPattern = true)
    {
        var stub = new Stub(status, body);
        var http = new HttpClient(stub) { BaseAddress = new Uri("https://api.iranpayamak.com/") };
        var o = new SmsOptions
        {
            Provider = "iranpayamak",
            LinkBase = "service.artepersia.com",
            IranPayamak = new IranPayamakOptions { ApiKey = "test-key-not-real", LineNumber = "3000505" },
        };
        if (withOtpPattern) o.IranPayamak.Templates["auth_otp"] = "XBk5FjbVlz";
        o.IranPayamak.ParamNames["code"] = "otp";
        var db = UnusedDb();
        var templates = new SmsTemplateService(db, new FixedClock(Daytime));
        var provider = new IranPayamakProvider(http, Options.Create(o), templates, db, new FixedClock(Daytime),
            NullLogger<IranPayamakProvider>.Instance);
        return (provider, stub);
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
    public async Task Missing_otp_pattern_does_not_call_the_provider()
    {
        var (sms, stub) = Make(HttpStatusCode.Created, "{}", withOtpPattern: false);
        var result = await sms.SendTemplateAsync("09121234567", MessageKeys.AuthOtp, Code, CancellationToken.None);
        Assert.False(result.Accepted);
        Assert.Null(stub.Request);
    }

    [Fact]
    public async Task Shop_message_uses_simple_api_with_filled_system_template()
    {
        var (sms, stub) = Make(HttpStatusCode.Created, """{"status":"success","data":99,"messages":null}""");
        var tokens = new Dictionary<string, string>
        {
            ["name"] = "علی", ["shop"] = "حکیمی", ["vehicle"] = "موتور", ["code"] = "abc12345",
        };
        // No TenantId: system default text, no DB hit for approved templates / licence.
        var result = await sms.SendAsync(new SmsRequest("09121234567", MessageKeys.CaseOpened, tokens), CancellationToken.None);

        Assert.True(result.Accepted);
        Assert.Equal("https://api.iranpayamak.com/ws/v1/sms/simple", stub.Request!.RequestUri!.ToString());
        var json = JsonDocument.Parse(stub.RequestBody!).RootElement;
        Assert.Equal("3000505", json.GetProperty("line_number").GetString());
        Assert.Equal("09121234567", json.GetProperty("recipients")[0].GetString());
        Assert.Contains("حکیمی", json.GetProperty("text").GetString());
        Assert.Contains("service.artepersia.com/t/abc12345", json.GetProperty("text").GetString());
    }
}
