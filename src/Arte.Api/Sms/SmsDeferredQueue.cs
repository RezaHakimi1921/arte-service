using System.Text.Json;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Sms;

/// <summary>Queues shop SMS when the branch has no credit; sends them after the next top-up.</summary>
public sealed class SmsDeferredQueue(ArteDbContext db, IClock clock, ILogger<SmsDeferredQueue> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task EnqueueAsync(SmsRequest request, CancellationToken ct)
    {
        if (request.TenantId is not { } tenantId) return;
        if (request.MessageKey == MessageKeys.AuthOtp) return;

        db.SmsDeferred.Add(new SmsDeferredMessage
        {
            TenantId = tenantId,
            Mobile = request.Mobile,
            MessageKey = request.MessageKey,
            TokensJson = JsonSerializer.Serialize(request.Tokens, Json),
            ExtraJson = request.Extra is { Count: > 0 } ? JsonSerializer.Serialize(request.Extra, Json) : null,
            CreatedAt = clock.UtcNow,
            Status = SmsDeferredStatuses.Pending,
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>After a branch is credited, try pending messages oldest-first (cap per call).</summary>
    public async Task<int> FlushAsync(Guid tenantId, ISmsProvider sms, int max = 30, CancellationToken ct = default)
    {
        var pending = await db.SmsDeferred
            .Where(m => m.TenantId == tenantId && m.Status == SmsDeferredStatuses.Pending)
            .OrderBy(m => m.CreatedAt)
            .Take(max)
            .ToListAsync(ct);
        var sent = 0;
        foreach (var row in pending)
        {
            var tokens = JsonSerializer.Deserialize<Dictionary<string, string>>(row.TokensJson, Json) ?? new();
            var extra = row.ExtraJson is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(row.ExtraJson, Json);
            row.Attempts++;
            var result = await sms.SendAsync(new SmsRequest(row.Mobile, row.MessageKey, tokens, tenantId, extra, QueueIfNoCredit: false), ct);
            if (result.Accepted)
            {
                row.Status = SmsDeferredStatuses.Sent;
                row.SentAt = clock.UtcNow;
                sent++;
            }
            else if (result.Error is SmsSendErrors.SkippedNoCredit or SmsWalletService.InsufficientCredit)
            {
                // Still no credit — leave pending for a later top-up.
                logger.LogInformation("Deferred SMS {Id} still waiting for credit on tenant {TenantId}", row.Id, tenantId);
                await db.SaveChangesAsync(ct);
                break;
            }
            else
            {
                row.Status = SmsDeferredStatuses.Failed;
                logger.LogWarning("Deferred SMS {Id} failed: {Error}", row.Id, result.Error);
            }
            await db.SaveChangesAsync(ct);
        }
        return sent;
    }
}
