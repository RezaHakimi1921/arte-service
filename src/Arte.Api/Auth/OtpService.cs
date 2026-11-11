using System.Security.Cryptography;
using System.Text;
using Arte.Api.Options;
using Arte.Api.Sms;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Arte.Api.Auth;

public enum OtpRequestResult { Sent, TooSoon, TooMany, SendFailed }

public sealed class OtpService(
    ArteDbContext db,
    ISmsProvider sms,
    SmsWalletService wallet,
    IClock clock,
    IOptions<OtpOptions> otpOptions,
    IOptions<SmsOptions> smsOptions,
    IHostEnvironment env,
    ILogger<OtpService> logger)
{
    private readonly OtpOptions _otp = otpOptions.Value;

    public async Task<OtpRequestResult> RequestAsync(string mobile, string? ip, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hourAgo = now.AddHours(-1);

        var recent = await db.OtpChallenges.AsNoTracking()
            .Where(c => c.Mobile == mobile && c.CreatedAt > hourAgo)
            .Select(c => c.CreatedAt)
            .ToListAsync(ct);
        if (recent.Count > 0 && recent.Max() > now.AddSeconds(-_otp.ResendCooldownSeconds)) return OtpRequestResult.TooSoon;
        if (recent.Count >= _otp.MaxPerMobilePerHour) return OtpRequestResult.TooMany;

        if (ip is not null)
        {
            var fromIp = await db.OtpChallenges.CountAsync(c => c.RequestIp == ip && c.CreatedAt > hourAgo, ct);
            if (fromIp >= _otp.MaxPerIpPerHour) return OtpRequestResult.TooMany;
        }

        // With the log-only provider in production, codes go only to the listed numbers.
        // Everyone else gets the same answer, so the endpoint does not reveal who is listed.
        if (env.IsProduction() && sms.Name == "fake" && !smsOptions.Value.FakeAllowedMobiles.Contains(mobile))
        {
            logger.LogInformation("OTP request for non-allowed mobile {Mobile} ignored", Mobile.Mask(mobile));
            db.OtpChallenges.Add(new OtpChallenge
            {
                Mobile = mobile, CodeHash = new string('0', 64), CreatedAt = now, ExpiresAt = now, ConsumedAt = now, RequestIp = ip,
            });
            await db.SaveChangesAsync(ct);
            return OtpRequestResult.Sent;
        }

        await db.OtpChallenges
            .Where(c => c.Mobile == mobile && c.ConsumedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), ct);

        // Never a leading zero: SMS patterns type the variable as a number and may drop it.
        var code = RandomNumberGenerator.GetInt32((int)Math.Pow(10, _otp.Digits - 1), (int)Math.Pow(10, _otp.Digits)).ToString();
        db.OtpChallenges.Add(new OtpChallenge
        {
            Mobile = mobile,
            CodeHash = HashCode(mobile, code),
            CreatedAt = now,
            ExpiresAt = now.AddSeconds(_otp.TtlSeconds),
            RequestIp = ip,
        });
        await db.SaveChangesAsync(ct);

        // OTP is paid by Arte's central SMS balance, never by a shop wallet.
        // Integration tests sync central to tiny values while asserting wallet math — skip charging there.
        SmsReservation? reservation = null;
        if (!env.IsEnvironment("Testing"))
        {
            var otpBody = SmsTags.Fill(SmsTemplates.OtpDefault, new Dictionary<string, string> { [SmsTags.Code] = code });
            var parts = Math.Max(1, SmsParts.Count(otpBody));
            string? walletError;
            (reservation, walletError) = await wallet.ReserveAsync(null, parts, MessageKeys.AuthOtp, ct);
            if (reservation is null)
            {
                logger.LogError("OTP blocked: central SMS credit insufficient ({Error})", walletError);
                await db.OtpChallenges.Where(c => c.Mobile == mobile && c.ConsumedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), ct);
                return OtpRequestResult.SendFailed;
            }
        }

        var sent = await sms.SendTemplateAsync(mobile, MessageKeys.AuthOtp, new Dictionary<string, string> { ["code"] = code }, ct);
        if (!sent.Accepted)
        {
            if (reservation is not null) await wallet.RefundAsync(reservation, ct);
            // The code never left the server: kill it, and tell the user instead of making them wait for nothing.
            await db.OtpChallenges.Where(c => c.Mobile == mobile && c.ConsumedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAt, now), ct);
            return OtpRequestResult.SendFailed;
        }
        return OtpRequestResult.Sent;
    }

    /// <summary>True only for the latest live code. Each wrong guess burns one attempt; the code dies after MaxAttempts.</summary>
    public async Task<bool> VerifyAsync(string mobile, string code, CancellationToken ct)
    {
        if (code.Length != _otp.Digits || !code.All(char.IsAsciiDigit)) return false;

        var now = clock.UtcNow;
        var challenge = await db.OtpChallenges
            .Where(c => c.Mobile == mobile && c.ConsumedAt == null && c.ExpiresAt > now)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (challenge is null) return false;

        challenge.Attempts++;
        var ok = CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(challenge.CodeHash), Encoding.ASCII.GetBytes(HashCode(mobile, code)));
        if (ok || challenge.Attempts >= _otp.MaxAttempts) challenge.ConsumedAt = now;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        return ok;
    }

    private string HashCode(string mobile, string code)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_otp.Pepper));
        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{mobile}:{code}")));
    }
}
