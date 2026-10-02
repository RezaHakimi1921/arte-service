using Arte.Api.Security;
using Arte.Core.Cases;
using Arte.Core.Common;
using Arte.Core.Data;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Cases;

/// <summary>
/// Photos on a case. Files are stored outside the web root under a server-generated name and only served
/// to members of the same business who can see the case; the type is checked from the file's bytes.
/// </summary>
public static class AttachmentEndpoints
{
    public const long MaxBytes = 6 * 1024 * 1024;

    private static readonly (string Type, string Ext, byte[] Magic, int Offset)[] Allowed =
    [
        ("image/jpeg", "jpg", [0xFF, 0xD8, 0xFF], 0),
        ("image/png", "png", [0x89, 0x50, 0x4E, 0x47], 0),
        ("image/webp", "webp", "WEBP"u8.ToArray(), 8),
    ];

    public static void MapAttachments(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/cases/{caseId:guid}/attachments", UploadAsync).RequireTenant();
        app.MapGet("/api/v1/attachments/{id:guid}", DownloadAsync).RequireTenant();
        app.MapDelete("/api/v1/attachments/{id:guid}", DeleteAsync).RequireTenant();
    }

    private static string Root(IConfiguration config, IHostEnvironment env) =>
        Path.GetFullPath(config["Storage:Root"] ?? Path.Combine(env.ContentRootPath, "uploads"));

    private static async Task<IResult> UploadAsync(Guid caseId, HttpRequest request, RequestUser me, ArteDbContext db,
        IClock clock, IConfiguration config, IHostEnvironment env, CancellationToken ct)
    {
        var m = me.RequiredMembership;
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == caseId, ct);
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
        if (!CaseAccess.CanWorkOn(m, c) && !m.Has(Arte.Core.Identity.Permissions.CasesCreate))
            return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

        // This endpoint alone accepts bodies larger than the global 1 MB limit.
        var sizeFeature = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = MaxBytes + 64 * 1024;
        if (!request.HasFormContentType) return Results.Problem(statusCode: 400, title: "فایلی ارسال نشده است.");

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0) return Results.Problem(statusCode: 400, title: "فایلی ارسال نشده است.");
        if (file.Length > MaxBytes) return Results.Problem(statusCode: 400, title: "حجم عکس حداکثر ۶ مگابایت است.");
        var caption = form["caption"].ToString().Trim();
        if (caption.Length > 200) caption = caption[..200];

        await using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var kind = Allowed.FirstOrDefault(a => bytes.Length >= a.Offset + a.Magic.Length && bytes.AsSpan(a.Offset, a.Magic.Length).SequenceEqual(a.Magic));
        if (kind.Type is null) return Results.Problem(statusCode: 400, title: "فقط عکس JPG، PNG یا WebP قابل بارگذاری است.");

        var stageKey = await db.Stages.Where(s => s.Id == c.StageId).Select(s => s.Key).SingleAsync(ct);
        var id = Guid.CreateVersion7();
        var relative = Path.Combine(c.TenantId.ToString("N"), c.Id.ToString("N"), $"{id:N}.{kind.Ext}");
        var full = Path.Combine(Root(config, env), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllBytesAsync(full, bytes, ct);

        var attachment = new CaseAttachment
        {
            Id = id, CaseId = c.Id, StageKey = stageKey, ContentType = kind.Type, SizeBytes = bytes.Length,
            StoragePath = relative.Replace('\\', '/'), Caption = caption.Length == 0 ? null : caption,
            CreatedBy = me.RequiredUserId, CreatedAt = clock.UtcNow,
        };
        db.CaseAttachments.Add(attachment);
        db.CaseEvents.Add(new CaseEvent
        {
            CaseId = c.Id, Type = "case.photo_added", ActorUserId = me.RequiredUserId, OccurredAt = attachment.CreatedAt,
            Data = System.Text.Json.JsonSerializer.SerializeToDocument(new { stage = stageKey, caption = attachment.Caption }),
        });
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/attachments/{id}", new { attachment.Id, attachment.StageKey, attachment.CreatedAt, attachment.Caption });
    }

    private static async Task<IResult> DownloadAsync(Guid id, RequestUser me, ArteDbContext db, IConfiguration config, IHostEnvironment env, CancellationToken ct)
    {
        var a = await db.CaseAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Results.NotFound();
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == a.CaseId, ct);
        if (c is null || !CaseAccess.CanSee(me.RequiredMembership, c)) return Results.NotFound();

        var root = Root(config, env);
        var full = Path.GetFullPath(Path.Combine(root, a.StoragePath));
        if (!full.StartsWith(root, StringComparison.Ordinal) || !File.Exists(full)) return Results.NotFound();
        return Results.File(full, a.ContentType, enableRangeProcessing: false);
    }

    private static async Task<IResult> DeleteAsync(Guid id, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct)
    {
        var a = await db.CaseAttachments.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (a is null) return Results.NotFound();
        var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == a.CaseId, ct);
        var m = me.RequiredMembership;
        if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
        if (a.CreatedBy != me.RequiredUserId && !m.Has(Arte.Core.Identity.Permissions.CasesCreate))
            return Results.Problem(statusCode: 403, title: "فقط فرستنده عکس یا مدیر می‌تواند آن را حذف کند.");
        a.DeletedAt = clock.UtcNow;
        a.DeletedBy = me.RequiredUserId;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
