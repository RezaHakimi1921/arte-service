using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Auth;

/// <summary>
/// Server-console only: docker compose run --rm -it api set-password &lt;username&gt; &lt;mobile&gt;
/// The password is typed at a hidden prompt; it never appears in shell history, arguments or logs.
/// </summary>
public static class SetPasswordCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args, TextReader? input = null)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: set-password <username> <mobile>");
            return 2;
        }

        var username = args[1].Trim().ToLowerInvariant();
        if (username.Length is < 3 or > 40 || !username.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '.' or '_' or '-'))
        {
            Console.Error.WriteLine("username: 3-40 chars, a-z 0-9 . _ -");
            return 2;
        }
        if (!Mobile.TryNormalize(args[2], out var mobile))
        {
            Console.Error.WriteLine("mobile is not a valid Iranian mobile number");
            return 2;
        }

        var password = ReadHidden("Password: ", input);
        if (password.Length < PasswordHasher.MinLength)
        {
            Console.Error.WriteLine($"password must be at least {PasswordHasher.MinLength} characters");
            return 2;
        }
        if (ReadHidden("Repeat:   ", input) != password)
        {
            Console.Error.WriteLine("passwords do not match");
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var user = await db.Users.SingleOrDefaultAsync(u => u.Mobile == mobile);
        if (await db.Users.AnyAsync(u => u.Username == username && u.Mobile != mobile))
        {
            Console.Error.WriteLine("username belongs to another mobile");
            return 1;
        }
        if (user is null)
        {
            user = new User { Mobile = mobile, CreatedAt = clock.UtcNow };
            db.Users.Add(user);
        }
        user.Username = username;
        user.PasswordHash = PasswordHasher.Hash(password);
        user.FailedPasswordAttempts = 0;
        user.PasswordLockedUntil = null;
        scope.ServiceProvider.GetRequiredService<Security.Audit>().Record("auth.password_set", null, user.Id, username);
        await db.SaveChangesAsync();

        Console.WriteLine($"Password set for '{username}' ({Mobile.Mask(mobile)}).");
        return 0;
    }

    private static string ReadHidden(string prompt, TextReader? input)
    {
        Console.Write(prompt);
        if (input is not null) return input.ReadLine() ?? "";
        if (Console.IsInputRedirected) return Console.ReadLine() ?? "";

        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (chars.Count > 0) chars.RemoveAt(chars.Count - 1); continue; }
            if (!char.IsControl(key.KeyChar)) chars.Add(key.KeyChar);
        }
        Console.WriteLine();
        return new string(chars.ToArray());
    }
}
