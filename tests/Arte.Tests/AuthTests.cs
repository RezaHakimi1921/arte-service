using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

namespace Arte.Tests;

[Collection(ApiCollection.Name)]
public sealed class AuthTests(ArteApiFactory api)
{
    [Fact]
    public async Task Otp_login_returns_access_token_and_sets_httponly_refresh_cookie()
    {
        var client = api.Client();
        var mobile = ArteApiFactory.NewMobile();
        (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile })).EnsureSuccessStatusCode();

        var res = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, code = api.Sms.Sent[mobile]["code"] });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var cookie = Assert.Single(res.Headers.GetValues("Set-Cookie"));
        Assert.Contains("arte_rt=", cookie);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Persian_digits_and_country_code_normalize_to_the_same_user()
    {
        var mobile = ArteApiFactory.NewMobile();
        var persian = "+98" + string.Concat(mobile[1..].Select(c => (char)('۰' + (c - '0'))));
        var client = api.Client();

        (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile = persian })).EnsureSuccessStatusCode();

        Assert.True(api.Sms.Sent.ContainsKey(mobile));
    }

    [Fact]
    public async Task Wrong_code_is_rejected_and_code_dies_after_max_attempts()
    {
        var client = api.Client();
        var mobile = ArteApiFactory.NewMobile();
        await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile });
        var real = api.Sms.Sent[mobile]["code"];
        var wrong = real == "000000" ? "111111" : "000000";

        for (var i = 0; i < 5; i++)
        {
            var bad = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, code = wrong });
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }

        var afterLockout = await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, code = real });
        Assert.Equal(HttpStatusCode.BadRequest, afterLockout.StatusCode);
    }

    [Fact]
    public async Task Code_cannot_be_used_twice()
    {
        var client = api.Client();
        var mobile = ArteApiFactory.NewMobile();
        await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile });
        var code = api.Sms.Sent[mobile]["code"];

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, code })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/auth/otp/verify", new { mobile, code })).StatusCode);
    }

    [Fact]
    public async Task Resend_within_cooldown_is_refused()
    {
        var client = api.Client();
        var mobile = ArteApiFactory.NewMobile();
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile })).StatusCode);
    }

    [Fact]
    public async Task Invalid_mobile_is_a_validation_error()
    {
        var res = await api.Client().PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile = "12345' OR 1=1--" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Refresh_rotates_and_replaying_an_old_token_revokes_the_family()
    {
        var (client, _) = await api.LoginAsync(ArteApiFactory.NewMobile());

        // T1: the token the client holds after this refresh.
        var stolen = await RefreshAndReadCookie(client);
        // T1 is rotated into T2.
        await RefreshAndReadCookie(client);

        // An attacker replays T1 after the grace window.
        using (var scope = api.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Arte.Core.Data.ArteDbContext>();
            var hash = Arte.Api.Auth.TokenService.Hash(stolen);
            await db.RefreshTokens.Where(t => t.TokenHash == hash)
                .ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, DateTimeOffset.UtcNow.AddMinutes(-2)));
        }
        var replay = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        replay.Headers.Add("Cookie", $"arte_rt={stolen}");
        var raw = api.Server.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await raw.SendAsync(replay)).StatusCode);

        // The legitimate session's T2 is now dead too.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task A_lost_refresh_response_does_not_log_the_user_out()
    {
        var (client, _) = await api.LoginAsync(ArteApiFactory.NewMobile());
        var held = await RefreshAndReadCookie(client);      // the token the browser holds
        await RefreshAndReadCookie(client);                  // rotated, but pretend the response never arrived

        var retry = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        retry.Headers.Add("Cookie", $"arte_rt={held}");
        Assert.Equal(HttpStatusCode.OK, (await api.Server.CreateClient().SendAsync(retry)).StatusCode);
        // And the session is still alive afterwards.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Logout_kills_the_refresh_token()
    {
        var (client, _) = await api.LoginAsync(ArteApiFactory.NewMobile());
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }

    [Theory]
    [InlineData("Bearer not-a-jwt")]
    [InlineData("Bearer eyJhbGciOiJub25lIiwidHlwIjoiSldUIn0.eyJzdWIiOiIwMTk5OTk5OS0wMDAwLTcwMDAtODAwMC0wMDAwMDAwMDAwMDAifQ.")]
    public async Task Forged_or_unsigned_tokens_are_rejected(string header)
    {
        var client = api.Client();
        client.DefaultRequestHeaders.Add("Authorization", header);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [Fact]
    public async Task Responses_carry_security_headers_and_no_server_banner()
    {
        var res = await api.Client().GetAsync("/health");
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.False(res.Headers.Contains("Server"));
    }

    private static async Task<string> RefreshAndReadCookie(HttpClient client)
    {
        var res = await client.PostAsync("/api/v1/auth/refresh", null);
        res.EnsureSuccessStatusCode();
        return res.Headers.GetValues("Set-Cookie").Single().Split(';')[0]["arte_rt=".Length..];
    }
}
