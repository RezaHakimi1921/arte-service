using System.Text.Json;
using Arte.Core.Cases;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Messaging;
using Arte.Core.Surveys;
using Arte.Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Surveys;

/// <summary>
/// Sends the survey SMS that are due, once a minute. Each invite is claimed first (a conditional update), so it is
/// never sent twice even if two runs overlap; a failed send is retried a few times, then given up and written to the
/// case timeline. Survey SMS go out only in sending hours (09:00–23:00 Tehran).
/// </summary>
public sealed class SurveySender(IServiceScopeFactory scopes, IClock clock, ILogger<SurveySender> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StaleClaim = TimeSpan.FromMinutes(10);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Survey sender run failed");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>One pass over the due invites. Returns how many SMS were accepted by the provider. Tests pass the time.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct, DateTimeOffset? at = null)
    {
        var now = at ?? clock.UtcNow;
        List<(Guid Id, Guid TenantId)> due;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
            var rows = await db.SurveyInvites.IgnoreQueryFilters([ArteDbContext.TenantFilter]).AsNoTracking()
                .Where(i => i.SentAt == null && i.CancelledAt == null && i.AnsweredAt == null && i.DueAt <= now
                            && i.Attempts < SurveyRules.MaxAttempts && (i.ClaimedAt == null || i.ClaimedAt < now - StaleClaim))
                .OrderBy(i => i.DueAt).Take(100).Select(i => new { i.Id, i.TenantId }).ToListAsync(ct);
            due = rows.Select(r => (r.Id, r.TenantId)).ToList();
        }

        var sent = 0;
        foreach (var (id, tenantId) in due)
        {
            try
            {
                if (await SendOneAsync(id, tenantId, now, ct)) sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Survey SMS for invite {InviteId} failed", id);
            }
        }
        return sent;
    }

    private async Task<bool> SendOneAsync(Guid id, Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        sp.GetRequiredService<TenantContext>().Set(tenantId);
        var db = sp.GetRequiredService<ArteDbContext>();

        // Claim: only one run gets past this line for a given invite.
        var claimed = await db.SurveyInvites
            .Where(i => i.Id == id && i.SentAt == null && i.CancelledAt == null && i.AnsweredAt == null
                        && (i.ClaimedAt == null || i.ClaimedAt < now - StaleClaim))
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.ClaimedAt, now), ct);
        if (claimed == 0) return false;

        var invite = await db.SurveyInvites.SingleAsync(i => i.Id == id, ct);
        var t = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == tenantId, ct);
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == invite.CaseId, ct);   // null when deleted

        // Switched off, branch closed or case gone since delivery: do not send.
        if (!t.SurveyEnabled || !t.SurveySendOn || !t.IsActive || c is null || c.TrackingCode is null)
        {
            invite.CancelledAt = now;
            invite.ClaimedAt = null;
            await db.SaveChangesAsync(ct);
            return false;
        }

        // Outside sending hours (e.g. delivered late at night): wait for the morning.
        var window = SurveyRules.IntoSendingHours(now);
        if (window > now)
        {
            invite.DueAt = window;
            invite.ClaimedAt = null;
            await db.SaveChangesAsync(ct);
            return false;
        }

        var customer = await db.Customers.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
            .SingleAsync(x => x.Id == c.CustomerId, ct);
        var tokens = new Dictionary<string, string>
        {
            ["shop"] = Cut(t.Name, 40),
            ["name"] = Cut(string.IsNullOrWhiteSpace(customer.FullName) ? "مشتری" : customer.FullName.Trim(), 60),
            ["code"] = c.TrackingCode,
        };
        var sms = sp.GetRequiredService<ISmsProvider>();
        var result = await sms.SendTemplateAsync(customer.Mobile, MessageKeys.SurveyRequest, tokens, ct);

        invite.Attempts++;
        invite.ClaimedAt = null;
        if (result.Accepted)
        {
            invite.SentAt = now;
            invite.LastError = null;
        }
        else
        {
            invite.LastError = Cut(result.Error ?? "failed", 200);
            // Not set up yet: no retries (the customer can still answer from the tracking page).
            if (result.Error == "template_not_configured") invite.Attempts = SurveyRules.MaxAttempts;
            else invite.DueAt = now.AddMinutes(10 * invite.Attempts);
        }

        if (result.Accepted || invite.Attempts >= SurveyRules.MaxAttempts)
            db.CaseEvents.Add(new CaseEvent
            {
                TenantId = tenantId, CaseId = c.Id, Type = "customer.sms", OccurredAt = now,
                Data = JsonSerializer.SerializeToDocument(new { Kind = MessageKeys.SurveyRequest, Sent = result.Accepted }, Json),
            });
        await db.SaveChangesAsync(ct);
        return result.Accepted;
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..max].TrimEnd();
}
