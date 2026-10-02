using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arte.Api.Sms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Arte.Tests;

/// <summary>
/// Runs the real API against a real Postgres (Testcontainers), so tenant filters, xmin concurrency
/// and migrations are exercised exactly as in production.
/// Set ARTE_TEST_DB to an existing connection string to skip the container.
/// </summary>
public sealed class ArteApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string InviteCode = "test-invite-code";

    private PostgreSqlContainer? _pg;
    private string _connectionString = Environment.GetEnvironmentVariable("ARTE_TEST_DB") ?? "";

    public async Task InitializeAsync()
    {
        if (string.IsNullOrEmpty(_connectionString))
        {
            _pg = new PostgreSqlBuilder("postgres:17-alpine").Build();
            await _pg.StartAsync();
            _connectionString = _pg.GetConnectionString();
        }
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        if (_pg is not null) await _pg.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", _connectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Jwt:Key", Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i * 7 + 3)).ToArray()));
        builder.UseSetting("Otp:Pepper", "test-pepper-test-pepper-test-pepper-1234");
        builder.UseSetting("Signup:InviteCode", InviteCode);
        // Tests log in many users from one IP; the per-IP limits have their own test.
        builder.UseSetting("RateLimits:AuthPerMinute", "100000");
        builder.UseSetting("RateLimits:GlobalPerMinute", "100000");
        builder.UseSetting("Otp:MaxPerIpPerHour", "100000");
    }

    public FakeSmsProvider Sms => Services.GetRequiredService<FakeSmsProvider>();

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = true,
    });

    private static int _mobileSeq = 1_000_000;

    public static string NewMobile() => $"0912{Interlocked.Increment(ref _mobileSeq):D7}";

    /// <summary>Logs in with OTP; returns a client carrying the access token.</summary>
    public async Task<(HttpClient Client, JsonElement Session)> LoginAsync(string mobile)
    {
        var client = Client();
        (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile })).EnsureSuccessStatusCode();
        var code = Sms.Sent[mobile]["code"];
        var res = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, code });
        res.EnsureSuccessStatusCode();
        var session = await res.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return (client, session);
    }

    /// <summary>New owner with a new business, already selected.</summary>
    public async Task<(HttpClient Client, Guid TenantId)> NewBusinessAsync(string? mobile = null)
    {
        var (client, _) = await LoginAsync(mobile ?? NewMobile());
        var res = await client.PostAsJsonAsync("/api/v1/tenants", new { name = "موتورسازی تست", inviteCode = InviteCode });
        res.EnsureSuccessStatusCode();
        var tenantId = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await SelectAsync(client, tenantId);
        return (client, tenantId);
    }

    public static async Task SelectAsync(HttpClient client, Guid tenantId)
    {
        var res = await client.PostAsJsonAsync("/api/v1/auth/select-tenant", new { tenantId });
        res.EnsureSuccessStatusCode();
        var session = await res.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
    }
}

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ArteApiFactory>
{
    public const string Name = "api";
}
