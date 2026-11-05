using System.Text.Json;
using Arte.Api.Security;
using Arte.Core.Billing;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Microsoft.EntityFrameworkCore;
using WP = WebPush;

namespace Arte.Api.Push;

/// <summary>
/// Web Push to staff phones and browsers (VAPID). Off until <c>Push:PublicKey</c> and <c>Push:PrivateKey</c> are configured;
/// a failed push never breaks the action that triggered it, and subscriptions the push service reports as gone are removed.
/// </summary>
public sealed class PushNotifier(ArteDbContext db, IConfiguration config, ILogger<PushNotifier> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string? PublicKey => Enabled ? config["Push:PublicKey"] : null;
    public bool Enabled => !string.IsNullOrWhiteSpace(config["Push:PublicKey"]) && !string.IsNullOrWhiteSpace(config["Push:PrivateKey"]);

    /// <summary>Members of the current business who hold the permission, except the person who did the thing.</summary>
    public async Task<List<Guid>> RecipientsAsync(string permission, Guid? exceptUserId, CancellationToken ct)
    {
        var members = await db.Memberships.AsNoTracking().Where(m => m.IsActive).ToListAsync(ct);
        return members.Where(m => m.Has(permission) && m.UserId != exceptUserId).Select(m => m.UserId).Distinct().ToList();
    }

    public async Task NotifyAsync(IReadOnlyCollection<Guid> userIds, string title, string body, string url, CancellationToken ct)
    {
        if (!Enabled || userIds.Count == 0) return;
        try
        {
            var subs = await db.PushSubscriptions.Where(s => userIds.Contains(s.UserId)).ToListAsync(ct);
            if (subs.Count == 0) return;
            var client = new WP.WebPushClient();
            var vapid = new WP.VapidDetails(config["Push:Subject"] ?? "mailto:support@artepersia.com", config["Push:PublicKey"]!, config["Push:PrivateKey"]!);
            var payload = JsonSerializer.Serialize(new { title, body, url }, Json);
            foreach (var s in subs)
            {
                try { await client.SendNotificationAsync(new WP.PushSubscription(s.Endpoint, s.P256dh, s.Auth), payload, vapid, ct); }
                catch (WP.WebPushException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Gone or System.Net.HttpStatusCode.NotFound)
                {
                    db.PushSubscriptions.Remove(s);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Push to a subscription failed");
                }
            }
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Push notification failed");
        }
    }

    /// <summary>Tell the people who run the business something happened on a case.</summary>
    public async Task CaseEventAsync(Guid caseId, long caseNumber, Guid? actor, string title, string permission, CancellationToken ct)
    {
        if (!Enabled) return;
        await NotifyAsync(await RecipientsAsync(permission, actor, ct), title, $"پرونده CASE-{caseNumber}", $"/?case={caseId}", ct);
    }
}

public static class PushEndpoints
{
    public sealed record Subscribe(string? Endpoint, string? P256dh, string? Auth);
    public sealed record Unsubscribe(string? Endpoint);

    public static void MapPush(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/push").RequireTenant();

        g.MapGet("/config", (PushNotifier push) => Results.Ok(new { Enabled = push.Enabled, PublicKey = push.PublicKey }));

        g.MapPost("/subscribe", async (Subscribe req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var errors = new Dictionary<string, string[]>();
            if (string.IsNullOrWhiteSpace(req.Endpoint) || req.Endpoint.Length > 600 || !Uri.TryCreate(req.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                errors["endpoint"] = ["نشانی اعلان نامعتبر است."];
            if (string.IsNullOrWhiteSpace(req.P256dh) || req.P256dh.Length > 200) errors["p256dh"] = ["کلید نامعتبر است."];
            if (string.IsNullOrWhiteSpace(req.Auth) || req.Auth.Length > 100) errors["auth"] = ["کلید نامعتبر است."];
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            // One row per browser: signing in as someone else on the same device moves it.
            var existing = await db.PushSubscriptions.SingleOrDefaultAsync(s => s.Endpoint == req.Endpoint, ct);
            if (existing is null)
                db.PushSubscriptions.Add(new Arte.Core.Billing.PushSubscription
                {
                    UserId = me.RequiredUserId, Endpoint = req.Endpoint!, P256dh = req.P256dh!, Auth = req.Auth!, CreatedAt = clock.UtcNow,
                });
            else { existing.UserId = me.RequiredUserId; existing.P256dh = req.P256dh!; existing.Auth = req.Auth!; }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        g.MapPost("/unsubscribe", async (Unsubscribe req, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            var s = await db.PushSubscriptions.SingleOrDefaultAsync(x => x.Endpoint == req.Endpoint && x.UserId == me.RequiredUserId, ct);
            if (s is not null) { db.PushSubscriptions.Remove(s); await db.SaveChangesAsync(ct); }
            return Results.NoContent();
        });
    }
}
