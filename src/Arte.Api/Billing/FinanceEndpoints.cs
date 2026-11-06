using System.Text.Json;
using Arte.Api.Cases;
using Arte.Api.Security;
using Arte.Core.Billing;
using Arte.Core.Cases;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Billing;

/// <summary>Card readers, shop cards, and the receipts customers send from their tracking page.</summary>
public static class FinanceEndpoints
{
    public sealed record TerminalInput(string? Name, string? Bank, bool? IsActive);
    public sealed record CardInput(string? CardNumber, string? HolderName, string? Bank, bool? IsActive, bool? ShowOnTracking);
    public sealed record ApproveInput(long? AmountRials, string? Note);
    public sealed record RejectInput(string? Note);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static void MapFinance(this IEndpointRouteBuilder app)
    {
        var terminals = app.MapGroup("/api/v1/payment-terminals").RequireTenant();
        terminals.MapGet("/", async (bool? includeInactive, ArteDbContext db, RequestUser me, CancellationToken ct) =>
        {
            var m = me.RequiredMembership;
            if (!m.Has(Permissions.PaymentsRecord) && !m.Has(Permissions.SettingsManage)) return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");
            if (includeInactive == true && !m.Has(Permissions.SettingsManage)) return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");
            var q = db.PaymentTerminals.AsNoTracking().AsQueryable();
            if (includeInactive != true) q = q.Where(x => x.IsActive);
            return Results.Ok(await q.OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.Bank, x.IsActive }).ToListAsync(ct));
        });
        terminals.MapPost("/", async (TerminalInput req, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var errors = ValidateTerminal(req, isNew: true);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var item = new PaymentTerminal { Name = req.Name!.Trim(), Bank = Clean(req.Bank), CreatedAt = clock.UtcNow };
            db.PaymentTerminals.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/payment-terminals/{item.Id}", new { item.Id });
        }).RequirePermission(Permissions.SettingsManage);
        terminals.MapPatch("/{id:guid}", async (Guid id, TerminalInput req, ArteDbContext db, CancellationToken ct) =>
        {
            var item = await db.PaymentTerminals.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            var errors = ValidateTerminal(req, isNew: false);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (req.Name is not null) item.Name = req.Name.Trim();
            if (req.Bank is not null) item.Bank = Clean(req.Bank);
            if (req.IsActive is { } active) item.IsActive = active;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);

        var cards = app.MapGroup("/api/v1/bank-cards").RequireTenant();
        cards.MapGet("/", async (bool? includeInactive, ArteDbContext db, RequestUser me, CancellationToken ct) =>
        {
            var m = me.RequiredMembership;
            if (!m.Has(Permissions.PaymentsRecord) && !m.Has(Permissions.SettingsManage)) return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");
            if (includeInactive == true && !m.Has(Permissions.SettingsManage)) return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");
            var q = db.BankCards.AsNoTracking().AsQueryable();
            if (includeInactive != true) q = q.Where(x => x.IsActive);
            return Results.Ok(await q.OrderBy(x => x.HolderName).Select(x => new { x.Id, x.CardNumber, x.HolderName, x.Bank, x.IsActive, x.ShowOnTracking }).ToListAsync(ct));
        });
        cards.MapPost("/", async (CardInput req, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var errors = ValidateCard(req, isNew: true);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var item = new BankCard { CardNumber = Digits(req.CardNumber!), HolderName = req.HolderName!.Trim(), Bank = Clean(req.Bank), CreatedAt = clock.UtcNow };
            db.BankCards.Add(item);
            if (req.ShowOnTracking == true) await OnlyOneShown(db, item, ct);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/bank-cards/{item.Id}", new { item.Id });
        }).RequirePermission(Permissions.SettingsManage);
        cards.MapPatch("/{id:guid}", async (Guid id, CardInput req, ArteDbContext db, CancellationToken ct) =>
        {
            var item = await db.BankCards.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (item is null) return Results.NotFound();
            var errors = ValidateCard(req, isNew: false);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            if (req.CardNumber is not null) item.CardNumber = Digits(req.CardNumber);
            if (req.HolderName is not null) item.HolderName = req.HolderName.Trim();
            if (req.Bank is not null) item.Bank = Clean(req.Bank);
            if (req.IsActive is { } active) { item.IsActive = active; if (!active) item.ShowOnTracking = false; }
            if (req.ShowOnTracking is { } show)
            {
                if (show && !item.IsActive) return Results.ValidationProblem(new Dictionary<string, string[]> { ["showOnTracking"] = ["کارت غیرفعال را نمی‌شود نمایش داد."] });
                item.ShowOnTracking = show;
                if (show) await OnlyOneShown(db, item, ct);
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.SettingsManage);

        MapCustomerReceipts(app);
    }

    /// <summary>One card at a time is shown to customers.</summary>
    private static async Task OnlyOneShown(ArteDbContext db, BankCard keep, CancellationToken ct)
    {
        foreach (var other in await db.BankCards.Where(x => x.ShowOnTracking && x.Id != keep.Id).ToListAsync(ct)) other.ShowOnTracking = false;
        keep.ShowOnTracking = true;
    }

    private static Dictionary<string, string[]> ValidateTerminal(TerminalInput req, bool isNew)
    {
        var e = new Dictionary<string, string[]>();
        if (isNew && string.IsNullOrWhiteSpace(req.Name)) e["name"] = ["نام کارتخوان لازم است."];
        if (req.Name is { } n && (n.Trim().Length is 0 or > 60)) e["name"] = ["نام ۱ تا ۶۰ حرف."];
        if (req.Bank is { Length: > 60 }) e["bank"] = ["نام بانک حداکثر ۶۰ حرف."];
        return e;
    }

    private static Dictionary<string, string[]> ValidateCard(CardInput req, bool isNew)
    {
        var e = new Dictionary<string, string[]>();
        if (isNew && string.IsNullOrWhiteSpace(req.CardNumber)) e["cardNumber"] = ["شماره کارت لازم است."];
        if (req.CardNumber is not null && Digits(req.CardNumber).Length != 16) e["cardNumber"] = ["شماره کارت ۱۶ رقم است."];
        if (isNew && string.IsNullOrWhiteSpace(req.HolderName)) e["holderName"] = ["نام صاحب کارت لازم است."];
        if (req.HolderName is { } h && (h.Trim().Length is 0 or > 80)) e["holderName"] = ["نام ۱ تا ۸۰ حرف."];
        if (req.Bank is { Length: > 60 }) e["bank"] = ["نام بانک حداکثر ۶۰ حرف."];
        return e;
    }

    /// <summary>Latin digits only: Persian and Arabic digits are converted, everything else is dropped.</summary>
    internal static string Digits(string s) =>
        new(s.Select(ch => ch switch { >= '۰' and <= '۹' => (char)('0' + (ch - '۰')), >= '٠' and <= '٩' => (char)('0' + (ch - '٠')), _ => ch }).Where(char.IsAsciiDigit).ToArray());

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ───────── receipts from the customer ─────────

    private static void MapCustomerReceipts(IEndpointRouteBuilder app)
    {
        // Public: the tracking code is the key, like the rest of the customer page.
        app.MapPost("/api/v1/track/{code}/receipts", async (string code, HttpRequest request, IServiceScopeFactory scopes, IConfiguration config,
            IHostEnvironment env, CancellationToken ct) =>
        {
            if (!Arte.Api.Tracking.TrackingEndpoints.ValidCode(code)) return Results.NotFound();
            await using var scope = scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ArteDbContext>();
            var found = await db.Cases.IgnoreQueryFilters([ArteDbContext.TenantFilter]).AsNoTracking()
                .Where(c => c.TrackingCode == code && !c.IsSample && c.DeletedAt == null).Select(c => new { c.Id, c.TenantId }).SingleOrDefaultAsync(ct);
            if (found is null) return Results.NotFound();
            sp.GetRequiredService<TenantContext>().Set(found.TenantId);
            if (!await db.Tenants.AnyAsync(t => t.Id == found.TenantId && t.IsActive, ct)) return Results.NotFound();
            var c = await db.Cases.AsNoTracking().SingleAsync(x => x.Id == found.Id, ct);

            var sizeFeature = request.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = AttachmentEndpoints.MaxBytes + 64 * 1024;
            if (!request.HasFormContentType) return Results.Problem(statusCode: 400, title: "عکس رسید ارسال نشده است.");
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.Problem(statusCode: 400, title: "عکس رسید ارسال نشده است.");
            if (file.Length > AttachmentEndpoints.MaxBytes) return Results.Problem(statusCode: 400, title: "حجم عکس حداکثر ۶ مگابایت است.");
            long? claimed = null;
            if (long.TryParse(Digits(form["amountRials"].ToString()), out var amount))
            {
                if (amount is <= 0 or > 100_000_000_000) return Results.ValidationProblem(new Dictionary<string, string[]> { ["amountRials"] = ["مبلغ نامعتبر است."] });
                claimed = amount;
            }
            // A few waiting at a time is enough; this also stops a stranger from filling the disk through one code.
            if (await db.CustomerReceipts.CountAsync(x => x.CaseId == c.Id && x.Status == ReceiptStatuses.Pending, ct) >= 3)
                return Results.Problem(statusCode: 429, title: "چند رسید در انتظار بررسی دارید؛ لطفاً تا تأیید آن‌ها صبر کنید.");

            await using var buffer = new MemoryStream();
            await file.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            if (AttachmentEndpoints.Sniff(bytes) is not { } kind) return Results.Problem(statusCode: 400, title: "فقط عکس JPG، PNG یا WebP قابل بارگذاری است.");

            var stageKey = await db.Stages.Where(s => s.Id == c.StageId).Select(s => s.Key).SingleAsync(ct);
            var now = sp.GetRequiredService<IClock>().UtcNow;
            var id = Guid.CreateVersion7();
            var relative = Path.Combine(c.TenantId.ToString("N"), c.Id.ToString("N"), $"{id:N}.{kind.Ext}");
            var full = Path.Combine(AttachmentEndpoints.Root(config, env), relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            await File.WriteAllBytesAsync(full, bytes, ct);

            db.CaseAttachments.Add(new CaseAttachment
            {
                Id = id, CaseId = c.Id, StageKey = stageKey, ContentType = kind.Type, SizeBytes = bytes.Length, StoragePath = relative.Replace('\\', '/'),
                Purpose = AttachmentPurposes.CustomerReceipt, CreatedBy = Guid.Empty, CreatedAt = now,
            });
            var receipt = new CustomerReceipt { CaseId = c.Id, AttachmentId = id, ClaimedRials = claimed, CreatedAt = now };
            db.CustomerReceipts.Add(receipt);
            db.CaseEvents.Add(new CaseEvent
            {
                TenantId = c.TenantId, CaseId = c.Id, Type = CaseEventTypes.ReceiptUploaded, OccurredAt = now,
                Data = JsonSerializer.SerializeToDocument(new { claimedRials = claimed }, Json),
            });
            await db.SaveChangesAsync(ct);
            await sp.GetRequiredService<Arte.Api.Push.PushNotifier>().CaseEventAsync(c.Id, c.Number, null, "رسید پرداخت جدید از مشتری", Permissions.PaymentsRecord, ct);
            return Results.Created($"/api/v1/track/{code}", new { receipt.Id });
        }).AllowAnonymous().RequireRateLimiting("session");

        app.MapGet("/api/v1/cases/{id:guid}/customer-receipts", async (Guid id, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            var m = me.RequiredMembership;
            var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
            if (!m.Has(Permissions.PaymentsRecord)) return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");
            return Results.Ok(await db.CustomerReceipts.AsNoTracking().Where(x => x.CaseId == id).OrderByDescending(x => x.CreatedAt)
                .Select(x => new
                {
                    x.Id, x.AttachmentId, x.ClaimedRials, x.Status, x.CreatedAt, x.ReviewedAt, x.ReviewNote,
                    // What the shop recorded from this receipt (it may be only part of the invoice).
                    PaidRials = db.Payments.Where(p => p.Id == x.PaymentId).Select(p => (long?)p.AmountRials).FirstOrDefault(),
                }).ToListAsync(ct));
        }).RequireTenant();

        app.MapPost("/api/v1/customer-receipts/{id:guid}/approve", async (Guid id, ApproveInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var m = me.RequiredMembership;
            var r = await db.CustomerReceipts.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            var c = await db.Cases.SingleOrDefaultAsync(x => x.Id == r.CaseId, ct);
            if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
            if (!m.Has(Permissions.PaymentsRecord)) return Results.Problem(statusCode: 403, title: "ثبت پرداخت برای شما فعال نیست.");
            if (r.Status != ReceiptStatuses.Pending) return Results.Problem(statusCode: 409, title: "این رسید قبلاً بررسی شده است.");
            var amount = req.AmountRials ?? r.ClaimedRials;
            if (amount is not > 0 || amount > 100_000_000_000)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["amountRials"] = ["مبلغ پرداخت را وارد کنید."] });
            if (req.Note is { Length: > 200 }) return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = ["حداکثر ۲۰۰ حرف."] });

            var now = clock.UtcNow;
            var attachment = await db.CaseAttachments.SingleAsync(a => a.Id == r.AttachmentId, ct);
            attachment.Purpose = AttachmentPurposes.Receipt;
            var payment = new Payment
            {
                CaseId = c.Id, CustomerId = c.CustomerId, AmountRials = amount.Value, Method = PaymentMethods.Transfer, PaidAt = now,
                RecordedBy = me.RequiredUserId, Note = string.IsNullOrWhiteSpace(req.Note) ? "رسید ارسال‌شده توسط مشتری" : req.Note.Trim(), ReceiptId = attachment.Id,
            };
            db.Payments.Add(payment);
            r.Status = ReceiptStatuses.Approved; r.ReviewedBy = me.RequiredUserId; r.ReviewedAt = now; r.PaymentId = payment.Id;
            db.CaseEvents.Add(new CaseEvent
            {
                TenantId = c.TenantId, CaseId = c.Id, Type = CaseEventTypes.PaymentRecorded, ActorUserId = me.RequiredUserId, OccurredAt = now,
                Data = JsonSerializer.SerializeToDocument(new { payment.AmountRials, payment.Method, payment.Note, Receipt = true, FromCustomer = true }, Json),
            });
            db.CaseEvents.Add(new CaseEvent
            {
                TenantId = c.TenantId, CaseId = c.Id, Type = CaseEventTypes.ReceiptReviewed, ActorUserId = me.RequiredUserId, OccurredAt = now,
                Data = JsonSerializer.SerializeToDocument(new { status = r.Status }, Json),
            });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireTenant();

        app.MapPost("/api/v1/customer-receipts/{id:guid}/reject", async (Guid id, RejectInput req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var m = me.RequiredMembership;
            var r = await db.CustomerReceipts.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (r is null) return Results.NotFound();
            var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.CaseId, ct);
            if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
            if (!m.Has(Permissions.PaymentsRecord)) return Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");
            if (r.Status != ReceiptStatuses.Pending) return Results.Problem(statusCode: 409, title: "این رسید قبلاً بررسی شده است.");
            if (req.Note is { Length: > 200 }) return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = ["حداکثر ۲۰۰ حرف."] });

            var now = clock.UtcNow;
            r.Status = ReceiptStatuses.Rejected; r.ReviewedBy = me.RequiredUserId; r.ReviewedAt = now;
            r.ReviewNote = string.IsNullOrWhiteSpace(req.Note) ? null : req.Note.Trim();
            db.CaseEvents.Add(new CaseEvent
            {
                TenantId = c.TenantId, CaseId = c.Id, Type = CaseEventTypes.ReceiptReviewed, ActorUserId = me.RequiredUserId, OccurredAt = now,
                Data = JsonSerializer.SerializeToDocument(new { status = r.Status }, Json),
            });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireTenant();
    }
}
