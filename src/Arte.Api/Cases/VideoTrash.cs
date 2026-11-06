using System.Text.Json;
using Arte.Api.Security;
using Arte.Core.Cases;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Cases;

/// <summary>
/// A deleted video waits in the trash for a few days (the manager can look at it and restore it in Settings);
/// after that the system removes the file and the row for good. This is the only automatic deletion in the system.
/// </summary>
public sealed class VideoTrash(IServiceScopeFactory scopes, IClock clock, IConfiguration config, IHostEnvironment env, ILogger<VideoTrash> logger) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public const int DefaultDays = 3;
    public static TimeSpan Retention(IConfiguration config) => TimeSpan.FromDays(config.GetValue("Storage:VideoTrashDays", DefaultDays));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Every);
        do
        {
            try { await RunOnceAsync(stoppingToken); }
            catch (Exception ex) when (ex is not OperationCanceledException) { logger.LogError(ex, "Video trash purge failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Removes the videos deleted more than the retention time ago. Returns how many. Tests pass the time.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct, DateTimeOffset? at = null)
    {
        var cutoff = (at ?? clock.UtcNow) - Retention(config);
        List<(Guid Id, Guid TenantId)> expired;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
            expired = (await db.CaseAttachments.IgnoreQueryFilters([ArteDbContext.TenantFilter, ArteDbContext.SoftDeleteFilter]).AsNoTracking()
                .Where(a => a.DeletedAt != null && a.DeletedAt < cutoff && a.ContentType.StartsWith("video/"))
                .OrderBy(a => a.DeletedAt).Take(50).Select(a => new { a.Id, a.TenantId }).ToListAsync(ct)).Select(a => (a.Id, a.TenantId)).ToList();
        }

        var root = AttachmentEndpoints.Root(config, env);
        foreach (var (id, tenantId) in expired)
        {
            try
            {
                // One scope per video, bound to its business, like every other background job.
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantId);
                var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
                var a = await db.CaseAttachments.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).SingleAsync(x => x.Id == id, ct);
                var full = Path.GetFullPath(Path.Combine(root, a.StoragePath));
                if (full.StartsWith(root, StringComparison.Ordinal) && File.Exists(full)) File.Delete(full);
                db.CaseEvents.Add(new CaseEvent
                {
                    TenantId = tenantId, CaseId = a.CaseId, Type = CaseEventTypes.VideoPurged, OccurredAt = at ?? clock.UtcNow,
                    Data = JsonSerializer.SerializeToDocument(new { sizeBytes = a.SizeBytes }, JsonSerializerOptions.Web),
                });
                db.CaseAttachments.Remove(a);
                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Removing video {AttachmentId} failed", id);
            }
        }
        return expired.Count;
    }
}

public static class VideoTrashEndpoints
{
    public static void MapVideoTrash(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/trash/videos").RequireTenant().RequirePermission(Permissions.SettingsManage);

        g.MapGet("/", async (ArteDbContext db, IConfiguration config, CancellationToken ct) =>
        {
            var keep = VideoTrash.Retention(config);
            var rows = await (from a in db.CaseAttachments.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
                              join c in db.Cases.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking() on a.CaseId equals c.Id
                              where a.DeletedAt != null && a.ContentType.StartsWith("video/")
                              orderby a.DeletedAt descending
                              select new { a.Id, a.CaseId, c.Number, a.SizeBytes, a.DeletedAt, a.DeletedBy }).ToListAsync(ct);
            var ids = rows.Where(r => r.DeletedBy != null).Select(r => r.DeletedBy!.Value).Distinct().ToList();
            var names = await db.Memberships.AsNoTracking().Where(m => ids.Contains(m.UserId))
                .Select(m => new { m.UserId, Name = m.DisplayName ?? m.User!.Mobile }).ToDictionaryAsync(x => x.UserId, x => x.Name, ct);
            return Results.Ok(rows.Select(r => new
            {
                r.Id, r.CaseId, r.Number, r.SizeBytes, r.DeletedAt, PurgeAt = r.DeletedAt + keep,
                DeletedByName = r.DeletedBy is { } by ? names.GetValueOrDefault(by) : null,
            }));
        });

        // The file itself, so the manager can watch it before deciding.
        g.MapGet("/{id:guid}/file", async (Guid id, ArteDbContext db, IConfiguration config, IHostEnvironment env, CancellationToken ct) =>
        {
            var a = await db.CaseAttachments.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt != null && x.ContentType.StartsWith("video/"), ct);
            if (a is null) return Results.NotFound();
            var root = AttachmentEndpoints.Root(config, env);
            var full = Path.GetFullPath(Path.Combine(root, a.StoragePath));
            if (!full.StartsWith(root, StringComparison.Ordinal) || !File.Exists(full)) return Results.NotFound();
            return Results.File(full, a.ContentType, enableRangeProcessing: true);
        });

        g.MapPost("/{id:guid}/restore", async (Guid id, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var a = await db.CaseAttachments.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter])
                .SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt != null && x.ContentType.StartsWith("video/"), ct);
            if (a is null) return Results.NotFound();
            a.DeletedAt = null;
            a.DeletedBy = null;
            db.CaseEvents.Add(new CaseEvent
            {
                TenantId = a.TenantId, CaseId = a.CaseId, Type = CaseEventTypes.VideoRestored, ActorUserId = me.RequiredUserId, OccurredAt = clock.UtcNow,
                Data = JsonSerializer.SerializeToDocument(new { }, JsonSerializerOptions.Web),
            });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
