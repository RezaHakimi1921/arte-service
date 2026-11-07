using Arte.Core.Common;

namespace Arte.Api.Sms.SmartX;

/// <summary>
/// Server-console only: docker compose run --rm -T api smartx-test &lt;mobile&gt;
/// Sends one test message straight to SmartX (no wallet, no template) and prints the outcome, never a secret.
/// </summary>
public static class SmartXTestCommand
{
    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        if (args.Length != 2 || !Mobile.TryNormalize(args[1], out var mobile))
        {
            Console.Error.WriteLine("usage: smartx-test <mobile>");
            return 2;
        }
        var client = services.GetRequiredService<SmartXSmsClient>();
        try
        {
            var result = await client.SendAsync([new SmsRecipient(0, mobile, "آرته سرویس\nپیام آزمایشی اتصال به اسمارت.")]);
            foreach (var b in result.Batches)
                Console.WriteLine($"batch {b.BatchId}: {b.State}, HTTP {b.HttpStatus?.ToString() ?? "-"}, attempts {b.Attempts}{(b.Error is null ? "" : ", " + b.Error)}");
            if (result.Invalid.Count > 0) Console.WriteLine("the mobile or the text was not valid");
            Console.WriteLine(result.AllAccepted && result.Batches.Count > 0 ? "Accepted by SmartX (this is not delivery: check the phone)" : "NOT accepted");
            return result.AllAccepted && result.Batches.Count > 0 ? 0 : 1;
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine(ex.Message);   // lists the missing SmartXSms__ keys, never their values
            return 2;
        }
    }
}
