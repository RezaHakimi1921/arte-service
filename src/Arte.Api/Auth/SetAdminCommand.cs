using Arte.Core.Common;
using Arte.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Auth;

/// <summary>
/// Server-console only: docker compose run --rm api set-admin &lt;mobile&gt; [off]
/// Gives (or takes away) access to the platform admin panel. Never possible from the API.
/// </summary>
public static class SetAdminCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        if (args.Length is < 2 or > 3 || !Mobile.TryNormalize(args[1], out var mobile))
        {
            Console.Error.WriteLine("usage: set-admin <mobile> [off]");
            return 2;
        }
        var on = args.Length == 2 || args[2] != "off";
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
        var user = await db.Users.SingleOrDefaultAsync(u => u.Mobile == mobile);
        if (user is null)
        {
            Console.Error.WriteLine("no user with this mobile; sign in once first");
            return 1;
        }
        user.IsPlatformAdmin = on;
        await db.SaveChangesAsync();
        Console.WriteLine(on ? $"{Mobile.Mask(mobile)} is now a platform admin" : $"{Mobile.Mask(mobile)} is no longer a platform admin");
        return 0;
    }
}
