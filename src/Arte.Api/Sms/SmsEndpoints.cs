using Arte.Api.Security;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Sms;

/// <summary>
/// SMS credit and message texts. The platform admin manages the central balance, every branch's credit and the
/// branch texts; a branch owner only sees their own balance.
/// </summary>
public static class SmsEndpoints
{
    public sealed record AmountInput(long? Parts, long? AmountRial, string? Note);
    public sealed record SyncInput(long? Parts, string? Note);
    public sealed record PricesInput(long? PurchasePricePerPartRial, long? SalePricePerPartRial, long? LowCentralParts);
    public sealed record TextInput(string? Text);
    public sealed record ChangeRequestInput(string? Text, string? Note);
    public sealed record ReviewInput(string? Note);

    public static void MapSms(this IEndpointRouteBuilder app)
    {
        // ───────── the branch's own balance + template preview / change request ─────────
        app.MapGet("/api/v1/sms/wallet", async (RequestUser me, SmsWalletService wallet, CancellationToken ct) =>
        {
            var tid = me.RequiredMembership.TenantId;
            var parts = await wallet.BranchBalanceAsync(tid, ct);
            var status = await wallet.StatusAsync(ct);
            return Results.Ok(new
            {
                Parts = parts,
                SalePricePerPartRial = status.SalePricePerPartRial,
                ValueRial = parts * status.SalePricePerPartRial,
            });
        }).RequirePermission(Permissions.SettingsManage);

        app.MapGet("/api/v1/sms/templates", async (RequestUser me, SmsTemplateService svc, CancellationToken ct) =>
        {
            var tid = me.RequiredMembership.TenantId;
            return Results.Ok(new
            {
                Templates = await svc.ListAsync(tid, ct),
                Tags = SmsTags.Titles.Where(t => t.Key != SmsTags.Code).Select(t => new { Tag = "{{" + t.Key + "}}", Title = t.Value }),
                MaxLength = SmsTemplates.MaxLength,
            });
        }).RequirePermission(Permissions.SettingsManage);

        app.MapPost("/api/v1/sms/templates/{key}/change-requests", async (string key, ChangeRequestInput req, RequestUser me,
            SmsTemplateService svc, Audit audit, ArteDbContext db, CancellationToken ct) =>
        {
            var tid = me.RequiredMembership.TenantId;
            var (row, error) = await svc.RequestChangeAsync(tid, key, req.Text ?? "", req.Note, me.RequiredUserId, ct);
            if (error is not null) return Fail("invalid_text", error);
            audit.Record("sms.template_change_requested", tid, me.RequiredUserId, key);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/sms/templates/{key}/change-requests/{row!.Id}", new { row.Id, row.Status });
        }).RequirePermission(Permissions.SettingsManage);

        // ───────── platform admin ─────────
        var admin = app.MapGroup("/api/v1/admin/sms").RequirePlatformAdmin();

        admin.MapGet("", async (SmsWalletService wallet, SmsTemplateService templates, ArteDbContext db, CancellationToken ct) =>
        {
            var status = await wallet.StatusAsync(ct);
            var branches = await db.SmsWallets.AsNoTracking()
                .Join(db.Tenants.AsNoTracking(), w => w.TenantId, t => t.Id, (w, t) => new { t.Id, t.Name, w.BalanceParts })
                .OrderByDescending(x => x.BalanceParts).Take(300).ToListAsync(ct);
            var ledger = await db.SmsLedger.AsNoTracking().Where(l => l.TenantId == null || l.Kind == SmsLedgerKinds.Credit)
                .OrderByDescending(l => l.CreatedAt).Take(30)
                .Select(l => new { l.Id, l.Kind, l.Parts, l.CentralAfter, l.AmountRial, l.Note, l.CreatedAt, l.TenantId }).ToListAsync(ct);

            var since = DateTimeOffset.UtcNow.AddDays(-30);
            var spends = await db.SmsLedger.AsNoTracking()
                .Where(l => l.CreatedAt >= since && (l.Kind == SmsLedgerKinds.Spend || l.Kind == SmsLedgerKinds.SpendArte))
                .GroupBy(l => new { l.Kind, l.MessageKey })
                .Select(g => new
                {
                    g.Key.Kind, g.Key.MessageKey,
                    Messages = g.Count(),
                    Parts = g.Sum(x => -x.Parts),
                }).ToListAsync(ct);
            var report = new
            {
                Days = 30,
                PurchasePricePerPartRial = status.PurchasePricePerPartRial,
                SalePricePerPartRial = status.SalePricePerPartRial,
                ProfitPerPartRial = status.ProfitPerPartRial,
                BranchParts = spends.Where(s => s.Kind == SmsLedgerKinds.Spend).Sum(s => s.Parts),
                ArteParts = spends.Where(s => s.Kind == SmsLedgerKinds.SpendArte).Sum(s => s.Parts),
                BranchMessages = spends.Where(s => s.Kind == SmsLedgerKinds.Spend).Sum(s => s.Messages),
                ArteMessages = spends.Where(s => s.Kind == SmsLedgerKinds.SpendArte).Sum(s => s.Messages),
                ByKey = spends.OrderByDescending(s => s.Parts).Select(s => new
                {
                    s.Kind, s.MessageKey, s.Messages, s.Parts,
                    SaleRial = s.Kind == SmsLedgerKinds.Spend ? s.Parts * status.SalePricePerPartRial : 0,
                    CostRial = s.Parts * status.PurchasePricePerPartRial,
                }),
            };
            var pendingTemplateRequests = await templates.PendingRequestCountAsync(ct);
            return Results.Ok(new { Status = status, Branches = branches, Ledger = ledger, Report = report, PendingTemplateRequests = pendingTemplateRequests });
        });

        admin.MapGet("/template-requests", async (string? status, SmsTemplateService svc, CancellationToken ct) =>
            Results.Ok(await svc.ListRequestsAsync(status, ct)));

        admin.MapPost("/template-requests/{id:guid}/approve", async (Guid id, SmsTemplateService svc, Audit audit, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            if (await svc.ApproveRequestAsync(id, me.RequiredUserId, ct) is { } error) return Fail("cannot_approve", error);
            audit.Record("admin.sms_template_request_approved", null, me.RequiredUserId, id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        admin.MapPost("/template-requests/{id:guid}/reject", async (Guid id, ReviewInput req, SmsTemplateService svc, Audit audit, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            if (await svc.RejectRequestAsync(id, me.RequiredUserId, req.Note, ct) is { } error) return Fail("cannot_reject", error);
            audit.Record("admin.sms_template_request_rejected", null, me.RequiredUserId, id.ToString());
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        admin.MapPut("/prices", async (PricesInput req, SmsWalletService wallet, Audit audit, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            if (req.PurchasePricePerPartRial is not { } buy || req.SalePricePerPartRial is not { } sell) return Fail("invalid_price", "قیمت خرید و فروش را وارد کنید.");
            return await Run(async () =>
            {
                await wallet.SetPricesAsync(buy, sell, req.LowCentralParts, ct);
                audit.Record("admin.sms_prices", null, me.RequiredUserId, $"buy {buy} sell {sell}");
                await db.SaveChangesAsync(ct);
                return Results.Ok(await wallet.StatusAsync(ct));
            });
        });

        admin.MapPost("/central/topup", async (AmountInput req, SmsWalletService wallet, Audit audit, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            var st = await wallet.StatusAsync(ct);
            var parts = req.Parts ?? (req.AmountRial is { } a ? SmsWalletService.PartsFor(a, st.PurchasePricePerPartRial) : 0);
            return await Run(async () =>
            {
                await wallet.TopUpCentralAsync(parts, req.AmountRial, req.Note, me.RequiredUserId, ct);
                audit.Record("admin.sms_central_topup", null, me.RequiredUserId, $"{parts} parts");
                await db.SaveChangesAsync(ct);
                return Results.Ok(await wallet.StatusAsync(ct));
            });
        });

        admin.MapPost("/central/sync", async (SyncInput req, SmsWalletService wallet, Audit audit, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            if (req.Parts is not { } parts) return Fail("invalid_parts", "موجودی پنل اسمارت را (به پارت) وارد کنید.");
            return await Run(async () =>
            {
                await wallet.SyncCentralAsync(parts, req.Note, me.RequiredUserId, ct);
                audit.Record("admin.sms_central_sync", null, me.RequiredUserId, $"{parts} parts");
                await db.SaveChangesAsync(ct);
                return Results.Ok(await wallet.StatusAsync(ct));
            });
        });

        admin.MapGet("/businesses/{id:guid}", async (Guid id, SmsWalletService wallet, ArteDbContext db, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
            var ledger = await db.SmsLedger.AsNoTracking().Where(l => l.TenantId == id).OrderByDescending(l => l.CreatedAt).Take(50)
                .Select(l => new { l.Id, l.Kind, l.Parts, l.BalanceAfter, l.AmountRial, l.MessageKey, l.Note, l.CreatedAt }).ToListAsync(ct);
            return Results.Ok(new { Parts = await wallet.BranchBalanceAsync(id, ct), Status = await wallet.StatusAsync(ct), Ledger = ledger });
        });

        // Parts or an amount (÷ sale price); a negative number takes credit back.
        admin.MapPost("/businesses/{id:guid}/credit", async (Guid id, AmountInput req, SmsWalletService wallet, Audit audit, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            var st = await wallet.StatusAsync(ct);
            var parts = req.Parts ?? (req.AmountRial is { } a ? SmsWalletService.PartsFor(a, st.SalePricePerPartRial) : 0);
            return await Run(async () =>
            {
                var balance = await wallet.CreditBranchAsync(id, parts, req.AmountRial, req.Note, me.RequiredUserId, ct);
                audit.Record("admin.sms_credit", id, me.RequiredUserId, $"{parts} parts");
                await db.SaveChangesAsync(ct);
                return Results.Ok(new { Parts = balance, Status = await wallet.StatusAsync(ct) });
            });
        });

        // ───────── message texts ─────────
        admin.MapGet("/businesses/{id:guid}/templates", async (Guid id, SmsTemplateService svc, ArteDbContext db, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
            return Results.Ok(new
            {
                Templates = await svc.ListAsync(id, ct),
                Tags = SmsTags.Titles.Where(t => t.Key != SmsTags.Code).Select(t => new { Tag = "{{" + t.Key + "}}", Title = t.Value }),
                MaxLength = SmsTemplates.MaxLength,
            });
        });

        admin.MapPut("/businesses/{id:guid}/templates/{key}", async (Guid id, string key, TextInput req, SmsTemplateService svc, ArteDbContext db, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
            var error = await svc.SaveDraftAsync(id, key, req.Text ?? "", ct);
            return error is null ? Results.Ok(Preview(key, req.Text ?? "")) : Fail("invalid_text", error);
        });

        admin.MapPost("/businesses/{id:guid}/templates/{key}/approve", async (Guid id, string key, SmsTemplateService svc, Audit audit, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
            var error = await svc.ApproveAsync(id, key, me.RequiredUserId, ct);
            if (error is not null) return Fail("cannot_approve", error);
            audit.Record("admin.sms_template_approved", id, me.RequiredUserId, key);
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        admin.MapPost("/businesses/{id:guid}/templates/{key}/reset", async (Guid id, string key, SmsTemplateService svc, Audit audit, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            if (!await db.Tenants.AnyAsync(t => t.Id == id, ct)) return Results.NotFound();
            await svc.ResetAsync(id, key, ct);
            audit.Record("admin.sms_template_reset", id, me.RequiredUserId, key);
            await db.SaveChangesAsync(ct);
            return Results.Ok();
        });

        admin.MapPost("/preview/{key}", (string key, TextInput req) => Results.Ok(Preview(key, req.Text ?? "")));
    }

    private static object Preview(string key, string text)
    {
        var error = SmsTemplates.Validate(key, text.Trim());
        var body = SmsTags.Fill(text.Trim(), SmsTags.Samples);
        return new { Text = body, Parts = SmsParts.Count(body), Length = text.Trim().Length, Error = error };
    }

    private static IResult Fail(string code, string message) =>
        Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: message, extensions: new Dictionary<string, object?> { ["code"] = code });

    private static async Task<IResult> Run(Func<Task<IResult>> action)
    {
        try { return await action(); }
        catch (SmsWalletException ex) { return Fail(ex.Code, ex.Message); }
    }
}
