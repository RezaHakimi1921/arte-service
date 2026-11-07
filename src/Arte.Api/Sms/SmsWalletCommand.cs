using Arte.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Sms;

/// <summary>
/// Server-console only: docker compose run --rm -T api sms-wallet &lt;show | central-sync N | central-topup N | credit TENANT N&gt;
/// Amounts are message parts. TENANT is the business id or part of its name. The same rules as the admin panel apply.
/// </summary>
public static class SmsWalletCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        await using var scope = services.CreateAsyncScope();
        var wallet = scope.ServiceProvider.GetRequiredService<SmsWalletService>();
        var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
        try
        {
            switch (args.Skip(1).FirstOrDefault())
            {
                case "show":
                    Print(await wallet.StatusAsync(default));
                    var rows = await db.SmsWallets.AsNoTracking().Join(db.Tenants, w => w.TenantId, t => t.Id, (w, t) => new { t.Name, w.TenantId, w.BalanceParts }).ToListAsync();
                    foreach (var r in rows) Console.WriteLine($"  {r.Name} ({r.TenantId}): {r.BalanceParts:N0} parts");
                    return 0;
                case "central-sync" when args.Length == 3 && long.TryParse(args[2], out var newBalance):
                    await wallet.SyncCentralAsync(newBalance, "console", null, default);
                    Print(await wallet.StatusAsync(default));
                    return 0;
                case "central-topup" when args.Length == 3 && long.TryParse(args[2], out var added):
                    await wallet.TopUpCentralAsync(added, null, "console", null, default);
                    Print(await wallet.StatusAsync(default));
                    return 0;
                case "credit" when args.Length == 4 && long.TryParse(args[3], out var parts):
                    var term = args[2];
                    var tenants = Guid.TryParse(term, out var id)
                        ? await db.Tenants.AsNoTracking().Where(t => t.Id == id).Select(t => new { t.Id, t.Name }).ToListAsync()
                        : await db.Tenants.AsNoTracking().Where(t => t.Name.Contains(term)).Select(t => new { t.Id, t.Name }).ToListAsync();
                    if (tenants.Count != 1)
                    {
                        Console.Error.WriteLine(tenants.Count == 0 ? "no business matches" : "more than one business matches, use the id:");
                        foreach (var t in tenants) Console.Error.WriteLine($"  {t.Name}: {t.Id}");
                        return 1;
                    }
                    var balance = await wallet.CreditBranchAsync(tenants[0].Id, parts, null, "console", null, default);
                    Console.WriteLine($"{tenants[0].Name}: {balance:N0} parts");
                    Print(await wallet.StatusAsync(default));
                    return 0;
                default:
                    Console.Error.WriteLine("usage: sms-wallet show | central-sync <parts> | central-topup <parts> | credit <business id or name> <parts>");
                    return 2;
            }
        }
        catch (SmsWalletException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static void Print(SmsWalletStatus s)
    {
        Console.WriteLine($"central {s.CentralParts:N0} parts, branches hold {s.CommittedParts:N0}, free {s.HeadroomParts:N0}");
        Console.WriteLine($"buy {s.PurchasePricePerPartRial / 10:N0} toman, sell {s.SalePricePerPartRial / 10:N0} toman, profit {s.ProfitPerPartRial / 10:N0} toman per part");
        foreach (var w in s.Warnings) Console.WriteLine($"WARNING: {w}");
    }
}
