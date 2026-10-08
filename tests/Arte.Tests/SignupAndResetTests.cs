using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>Self sign-up (mobile + name, no invite code) and forgotten password by SMS code.</summary>
[Collection(ApiCollection.Name)]
public sealed class SignupAndResetTests(ArteApiFactory api)
{
    private const string NewPassword = "new-long-password-1";

    [Fact]
    public async Task Sign_up_needs_only_a_name_and_names_the_shop_after_the_owner()
    {
        var (client, _) = await api.LoginAsync(ArteApiFactory.NewMobile());

        var noName = await client.PostAsJsonAsync("/api/v1/tenants", new { });
        Assert.Equal(HttpStatusCode.BadRequest, noName.StatusCode);
        Assert.Contains("ownerName", await noName.Content.ReadAsStringAsync());

        var res = await client.PostAsJsonAsync("/api/v1/tenants", new { ownerName = "علی رضایی" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        var tenant = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("تعمیرگاه علی رضایی", tenant.GetProperty("name").GetString());

        var session = await (await client.PostAsJsonAsync("/api/v1/auth/select-tenant", new { tenantId = tenant.GetProperty("id").GetGuid() }))
            .Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("accessToken").GetString());
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal("علی رضایی", me.GetProperty("displayName").GetString());
    }

    private async Task<string> CodeFor(HttpClient client, string mobile)
    {
        (await client.PostAsJsonAsync("/api/v1/auth/otp/request", new { mobile })).EnsureSuccessStatusCode();
        return api.Sms.Sent[mobile]["code"];
    }

    [Fact]
    public async Task Reset_sets_the_password_signs_in_and_ends_other_sessions()
    {
        // A user created with a password (no SMS yet), signed in once: that is the "other session".
        const string oldPassword = "old-long-password-1";
        var mobile = ArteApiFactory.NewMobile();
        var username = "u" + Guid.NewGuid().ToString("N")[..10];
        var stdin = Console.In;
        Console.SetIn(new StringReader($"{oldPassword}\n{oldPassword}\n"));
        try { Assert.Equal(0, await Arte.Api.Auth.SetPasswordCommand.RunAsync(api.Services, ["set-password", username, mobile])); }
        finally { Console.SetIn(stdin); }
        var old = api.Client();
        Assert.Equal(HttpStatusCode.OK, (await old.PostAsJsonAsync("/api/v1/auth/password", new { username, password = oldPassword })).StatusCode);

        var client = api.Client();
        var code = await CodeFor(client, mobile);
        var res = await client.PostAsJsonAsync("/api/v1/auth/password/reset", new { mobile, code, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("arte_rt=", res.Headers.GetValues("Set-Cookie").Single());

        // The new password works with the mobile number as the identifier, Persian digits included; the old one does not.
        var persian = string.Concat(mobile.Select(c => (char)('۰' + (c - '0'))));
        Assert.Equal(HttpStatusCode.OK, (await api.Client().PostAsJsonAsync("/api/v1/auth/password", new { username = persian, password = NewPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await api.Client().PostAsJsonAsync("/api/v1/auth/password", new { username, password = oldPassword })).StatusCode);

        // The session from before the reset cannot refresh any more.
        Assert.Equal(HttpStatusCode.Unauthorized, (await old.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Reset_refuses_a_wrong_code_a_short_password_and_an_unknown_mobile()
    {
        var client = api.Client();
        var mobile = ArteApiFactory.NewMobile();
        var code = await CodeFor(client, mobile);

        var shortPw = await client.PostAsJsonAsync("/api/v1/auth/password/reset", new { mobile, code, newPassword = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, shortPw.StatusCode);
        Assert.Contains("newPassword", await shortPw.Content.ReadAsStringAsync());

        var wrong = await client.PostAsJsonAsync("/api/v1/auth/password/reset", new { mobile, code = code == "11111" ? "22222" : "11111", newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        // A valid code for a number that never signed up does not create an account.
        var fresh = ArteApiFactory.NewMobile();
        var freshCode = await CodeFor(client, fresh);
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/password/reset", new { mobile = fresh, code = freshCode, newPassword = NewPassword });
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }
}
