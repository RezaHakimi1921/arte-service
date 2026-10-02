using System.Net;
using System.Net.Http.Json;
using Arte.Api.Auth;

namespace Arte.Tests;

[Collection(ApiCollection.Name)]
public sealed class PasswordLoginTests(ArteApiFactory api)
{
    private const string Password = "a-long-test-password";

    private async Task<string> CreateUser()
    {
        var username = "u" + Guid.NewGuid().ToString("N")[..10];
        var stdin = Console.In;
        Console.SetIn(new StringReader($"{Password}\n{Password}\n"));
        try
        {
            Assert.Equal(0, await SetPasswordCommand.RunAsync(api.Services, ["set-password", username, ArteApiFactory.NewMobile()]));
        }
        finally
        {
            Console.SetIn(stdin);
        }
        return username;
    }

    [Fact]
    public async Task Correct_password_logs_in()
    {
        var username = await CreateUser();
        var res = await api.Client().PostAsJsonAsync("/api/v1/auth/password", new { username = username.ToUpperInvariant(), password = Password });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("arte_rt=", res.Headers.GetValues("Set-Cookie").Single());
    }

    [Fact]
    public async Task Unknown_user_and_wrong_password_get_the_same_answer()
    {
        var username = await CreateUser();
        var client = api.Client();
        var wrong = await client.PostAsJsonAsync("/api/v1/auth/password", new { username, password = "wrong-password-123" });
        var unknown = await client.PostAsJsonAsync("/api/v1/auth/password", new { username = "nobody-here", password = "wrong-password-123" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(await wrong.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_one()
    {
        var username = await CreateUser();
        var client = api.Client();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/v1/auth/password", new { username, password = "wrong-password-123" });

        var res = await client.PostAsJsonAsync("/api/v1/auth/password", new { username, password = Password });
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
    }

    [Fact]
    public async Task Short_passwords_are_refused_by_the_console_command()
    {
        var stdin = Console.In;
        Console.SetIn(new StringReader("short\nshort\n"));
        try
        {
            Assert.Equal(2, await SetPasswordCommand.RunAsync(api.Services, ["set-password", "shorty", ArteApiFactory.NewMobile()]));
        }
        finally
        {
            Console.SetIn(stdin);
        }
    }
}
