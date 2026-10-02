using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arte.Api.Auth;
using Arte.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arte.Tests;

[Collection(ApiCollection.Name)]
public sealed class OpenModeOffTests(ArteApiFactory api)
{
    [Fact]
    public async Task Open_sign_in_does_not_exist_when_open_mode_is_off()
    {
        var res = await api.Client().PostAsync("/api/v1/auth/open", null);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task Sessions_issued_in_open_mode_die_when_open_mode_is_off()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var userId = (await owner.GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").GetGuid();

        IssuedTokens issued;
        using (var scope = api.Services.CreateScope())
            issued = await scope.ServiceProvider.GetRequiredService<TokenService>().IssueAsync(userId, null, default, openMode: true);

        var client = api.Client();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", issued.AccessToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);

        var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        refresh.Headers.Add("Cookie", $"arte_rt={issued.RefreshToken}");
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Server.CreateClient().SendAsync(refresh)).StatusCode);
    }
}

[Collection(OpenModeCollection.Name)]
public sealed class OpenModeOnTests(OpenModeApiFactory api)
{
    private async Task<HttpClient> OpenAsync()
    {
        var client = api.Client();
        var res = await client.PostAsync("/api/v1/auth/open", null);
        res.EnsureSuccessStatusCode();
        var session = await res.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task Open_mode_signs_in_as_owner_of_one_business()
    {
        var a = await OpenAsync();
        var b = await OpenAsync();

        var meA = await a.GetFromJsonAsync<JsonElement>("/api/v1/me");
        var meB = await b.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.True(meA.GetProperty("openMode").GetBoolean());
        Assert.Equal("owner", meA.GetProperty("business").GetProperty("role").GetString());
        Assert.Equal(meA.GetProperty("business").GetProperty("tenantId").GetGuid(), meB.GetProperty("business").GetProperty("tenantId").GetGuid());
        Assert.Equal(HttpStatusCode.Created, (await a.PostAsJsonAsync("/api/v1/customers", new { mobile = ArteApiFactory.NewMobile() })).StatusCode);
    }

    [Fact]
    public async Task Credentials_are_set_once_freely_then_need_the_current_password()
    {
        var client = await OpenAsync();
        var username = "owner" + Guid.NewGuid().ToString("N")[..6];

        var first = await client.PutAsJsonAsync("/api/v1/account", new { username, newPassword = "first-password-123" });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Someone else with an open session cannot take the account over.
        var other = await OpenAsync();
        var hijack = await other.PutAsJsonAsync("/api/v1/account", new { newPassword = "attacker-password-1" });
        Assert.Equal(HttpStatusCode.BadRequest, hijack.StatusCode);

        var change = await client.PutAsJsonAsync("/api/v1/account",
            new { newPassword = "second-password-456", currentPassword = "first-password-123" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        var login = await api.Client().PostAsJsonAsync("/api/v1/auth/password", new { username, password = "second-password-456" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        // Leave the shared owner without a password so the other test is independent.
        using var scope = api.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ArteDbContext>().Users
            .Where(u => u.Mobile == OpenModeEndpoints.OwnerMobile)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PasswordHash, (string?)null).SetProperty(u => u.Username, (string?)null));
    }

    [Fact]
    public async Task Weak_password_is_refused()
    {
        var client = await OpenAsync();
        var res = await client.PutAsJsonAsync("/api/v1/account", new { username = "someone", newPassword = "reza1123" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }
}
