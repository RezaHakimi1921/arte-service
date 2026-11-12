using System.Text.Json;
using Arte.Api.Security;
using Arte.Core.Common;
using Arte.Core.Customers;
using Arte.Core.Data;
using Arte.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Customers;

public static class CustomerEndpoints
{
    public sealed record CustomerInput(string? Mobile, string? FullName, string? Notes);
    public sealed record AssetInput(string? Kind, string? Title, string? Identifier, Dictionary<string, string>? Attributes);

    private const int MaxAttributes = 20;

    public static void MapCustomers(this IEndpointRouteBuilder app)
    {
        var read = app.MapGroup("/api/v1/customers").RequireTenant();
        var write = app.MapGroup("/api/v1").RequirePermission(Permissions.CasesCreate);

        read.MapGet("/", async (string? q, int? page, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            if (!CanBrowse(me)) return Forbidden();

            var query = db.Customers.AsNoTracking();
            var term = q?.Trim();
            if (!string.IsNullOrEmpty(term))
            {
                if (term.Length > 60) return Results.ValidationProblem(new Dictionary<string, string[]> { ["q"] = ["عبارت جستجو طولانی است."] });
                var digits = Digits(term);
                if (digits.Length >= 3 && digits.Length == term.Count(c => !char.IsWhiteSpace(c) && c != '-'))
                {
                    var needle = digits.StartsWith("98") ? digits[2..] : digits;
                    query = query.Where(c => c.Mobile.Contains(needle));
                }
                else
                    query = query.Where(c => c.FullName != null && EF.Functions.ILike(c.FullName, $"%{EscapeLike(term)}%", "\\"));
            }

            var p = Math.Clamp(page ?? 1, 1, 1000);
            var items = await query
                .OrderByDescending(c => c.UpdatedAt)
                .Skip((p - 1) * 30).Take(30)
                .Select(c => new
                {
                    c.Id, c.Mobile, c.FullName,
                    AssetCount = c.Assets.Count,
                    VehicleTitle = c.Assets.OrderByDescending(a => a.CreatedAt).Select(a => a.Title).FirstOrDefault(),
                })
                .ToListAsync(ct);
            var stats = await StatsFor(db, items.Select(c => c.Id).ToList(), ct);
            return Results.Ok(items.Select(c =>
            {
                var s = stats[c.Id];
                return new { c.Id, c.Mobile, c.FullName, c.AssetCount, c.VehicleTitle, s.VisitCount, s.ValueRials };
            }));
        });

        read.MapGet("/{id:guid}", async (Guid id, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            if (!CanBrowse(me)) return Forbidden();
            var c = await db.Customers.AsNoTracking().Include(x => x.Assets).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            var stats = await StatsFor(db, [c.Id], ct);
            return Results.Ok(ToView(c, stats[c.Id]));
        });

        write.MapPost("/customers", async (CustomerInput req, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var errors = Validate(req, requireMobile: true, out var mobile);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var existing = await db.Customers.AsNoTracking().Where(c => c.Mobile == mobile).Select(c => c.Id).SingleOrDefaultAsync(ct);
            if (existing != Guid.Empty)
                return Results.Problem(statusCode: 409, title: "مشتری با این شماره وجود دارد.", extensions: new Dictionary<string, object?> { ["customerId"] = existing });

            var now = clock.UtcNow;
            var customer = new Customer
            {
                Mobile = mobile!, FullName = Clean(req.FullName), Notes = Clean(req.Notes), CreatedAt = now, UpdatedAt = now,
            };
            db.Customers.Add(customer);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                return Results.Problem(statusCode: 409, title: "مشتری با این شماره وجود دارد.");
            }
            return Results.Created($"/api/v1/customers/{customer.Id}", ToView(customer));
        });

        write.MapPatch("/customers/{id:guid}", async (Guid id, CustomerInput req, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var errors = Validate(req, requireMobile: false, out var mobile);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var c = await db.Customers.Include(x => x.Assets).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            if (mobile is not null && mobile != c.Mobile)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["mobile"] = ["شماره موبایل قابل تغییر نیست."] });
            if (req.FullName is not null) c.FullName = Clean(req.FullName);
            if (req.Notes is not null) c.Notes = Clean(req.Notes);
            c.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            var stats = await StatsFor(db, [c.Id], ct);
            return Results.Ok(ToView(c, stats[c.Id]));
        });

        write.MapPost("/customers/{id:guid}/assets", async (Guid id, AssetInput req, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            if (!await db.Customers.AnyAsync(c => c.Id == id, ct)) return Results.NotFound();
            var errors = ValidateAsset(req, isNew: true);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var asset = new Asset
            {
                CustomerId = id,
                Kind = req.Kind ?? AssetKinds.Vehicle,
                Title = req.Title!.Trim(),
                Identifier = Clean(req.Identifier),
                Attributes = ToJson(req.Attributes),
                CreatedAt = clock.UtcNow,
            };
            db.Assets.Add(asset);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/assets/{asset.Id}", AssetView(asset));
        });

        // Soft delete: the customer and their motorcycles disappear from lists and search, and can be restored from the trash.
        write.MapDelete("/customers/{id:guid}", async (Guid id, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var c = await db.Customers.Include(x => x.Assets).SingleOrDefaultAsync(x => x.Id == id, ct);
            if (c is null) return Results.NotFound();
            var now = clock.UtcNow;
            c.DeletedAt = now;
            c.DeletedBy = me.RequiredUserId;
            foreach (var a in c.Assets)
            {
                a.DeletedAt = now;
                a.DeletedBy = me.RequiredUserId;
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        write.MapPost("/customers/{id:guid}/restore", async (Guid id, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var c = await db.Customers.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter])
                .SingleOrDefaultAsync(x => x.Id == id && x.DeletedAt != null, ct);
            if (c is null) return Results.NotFound();
            if (await db.Customers.AnyAsync(x => x.Mobile == c.Mobile, ct))
                return Results.Problem(statusCode: 409, title: "مشتری فعال دیگری با همین شماره وجود دارد.");

            // Bring back the motorcycles that were deleted together with the customer, not ones deleted earlier on their own.
            var assets = await db.Assets.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter])
                .Where(a => a.CustomerId == id && a.DeletedAt == c.DeletedAt).ToListAsync(ct);
            foreach (var a in assets)
            {
                a.DeletedAt = null;
                a.DeletedBy = null;
            }
            c.DeletedAt = null;
            c.DeletedBy = null;
            c.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            var stats = await StatsFor(db, [c.Id], ct);
            return Results.Ok(ToView(c, stats[c.Id]));
        });

        write.MapGet("/customers/trash", async (ArteDbContext db, CancellationToken ct) =>
            Results.Ok(await db.Customers.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
                .Where(c => c.DeletedAt != null)
                .OrderByDescending(c => c.DeletedAt)
                .Take(100)
                .Select(c => new { c.Id, c.Mobile, c.FullName, c.DeletedAt })
                .ToListAsync(ct)));

        write.MapDelete("/assets/{id:guid}", async (Guid id, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var asset = await db.Assets.SingleOrDefaultAsync(a => a.Id == id, ct);
            if (asset is null) return Results.NotFound();
            asset.DeletedAt = clock.UtcNow;
            asset.DeletedBy = me.RequiredUserId;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        write.MapPost("/assets/{id:guid}/restore", async (Guid id, ArteDbContext db, CancellationToken ct) =>
        {
            var asset = await db.Assets.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter])
                .SingleOrDefaultAsync(a => a.Id == id && a.DeletedAt != null, ct);
            if (asset is null) return Results.NotFound();
            if (!await db.Customers.AnyAsync(c => c.Id == asset.CustomerId, ct))
                return Results.Problem(statusCode: 409, title: "ابتدا مشتری این وسیله را بازگردانید.");
            asset.DeletedAt = null;
            asset.DeletedBy = null;
            await db.SaveChangesAsync(ct);
            return Results.Ok(AssetView(asset));
        });

        write.MapPatch("/assets/{id:guid}",async (Guid id, AssetInput req, ArteDbContext db, CancellationToken ct) =>
        {
            var errors = ValidateAsset(req, isNew: false);
            if (errors.Count > 0) return Results.ValidationProblem(errors);

            var asset = await db.Assets.SingleOrDefaultAsync(a => a.Id == id, ct);
            if (asset is null) return Results.NotFound();
            if (req.Title is not null) asset.Title = req.Title.Trim();
            if (req.Kind is not null) asset.Kind = req.Kind;
            if (req.Identifier is not null) asset.Identifier = Clean(req.Identifier);
            if (req.Attributes is not null) asset.Attributes = ToJson(req.Attributes);
            await db.SaveChangesAsync(ct);
            return Results.Ok(AssetView(asset));
        });
    }

    /// <summary>Technicians see customers through their own cases, not by browsing the whole list.</summary>
    private static bool CanBrowse(RequestUser me) =>
        me.RequiredMembership.Has(Permissions.CasesCreate) || me.RequiredMembership.Has(Permissions.CasesViewAll);

    private static IResult Forbidden() => Results.Problem(statusCode: 403, title: "دسترسی لازم را ندارید.");

    private static Dictionary<string, string[]> Validate(CustomerInput req, bool requireMobile, out string? mobile)
    {
        var errors = new Dictionary<string, string[]>();
        mobile = null;
        if (req.Mobile is not null || requireMobile)
        {
            if (IsShopMobile(req.Mobile, out var m)) mobile = m;
            else errors["mobile"] = ["شماره باید با ۰۹ شروع شود و ۱۱ رقم باشد."];
        }
        if (req.FullName is { Length: > 120 }) errors["fullName"] = ["نام حداکثر ۱۲۰ حرف."];
        if (req.Notes is { Length: > 2000 }) errors["notes"] = ["یادداشت حداکثر ۲۰۰۰ حرف."];
        return errors;
    }

    /// <summary>Shop customers are stored only as 09 and exactly 11 digits. +98 and 10-digit forms are not rewritten here.</summary>
    private static bool IsShopMobile(string? input, out string mobile)
    {
        mobile = "";
        if (string.IsNullOrWhiteSpace(input) || input.Length > 16) return false;
        var digits = new string(input.Select(ch => ch switch
        {
            >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
            >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
            _ => ch,
        }).Where(char.IsAsciiDigit).ToArray());
        if (digits.Length != 11 || !digits.StartsWith("09", StringComparison.Ordinal)) return false;
        mobile = digits;
        return true;
    }

    private static Dictionary<string, string[]> ValidateAsset(AssetInput req, bool isNew)
    {
        var errors = new Dictionary<string, string[]>();
        if (isNew && string.IsNullOrWhiteSpace(req.Title)) errors["title"] = ["مدل وسیله لازم است."];
        if (req.Title is { Length: > 120 }) errors["title"] = ["حداکثر ۱۲۰ حرف."];
        if (req.Kind is not null && !AssetKinds.All.Contains(req.Kind)) errors["kind"] = ["نوع وسیله پشتیبانی نمی‌شود."];
        if (req.Identifier is { Length: > 60 }) errors["identifier"] = ["حداکثر ۶۰ حرف."];
        if (req.Attributes is { } a && (a.Count > MaxAttributes || a.Any(kv => kv.Key.Length is 0 or > 40 || kv.Value is null || kv.Value.Length > 100)))
            errors["attributes"] = [$"حداکثر {MaxAttributes} ویژگی، کلید تا ۴۰ و مقدار تا ۱۰۰ حرف."];
        return errors;
    }

    /// <summary>Visits are real cases. Value is money actually paid, in rials. Sample data is left out.</summary>
    private sealed record CustomerStats(int VisitCount, long ValueRials, DateTimeOffset? LastVisitAt);

    private static async Task<Dictionary<Guid, CustomerStats>> StatsFor(ArteDbContext db, IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];
        var visits = await db.Cases.AsNoTracking()
            .Where(k => ids.Contains(k.CustomerId) && !k.IsSample)
            .GroupBy(k => k.CustomerId)
            .Select(g => new { Id = g.Key, Count = g.Count(), Last = g.Max(k => k.OpenedAt) })
            .ToListAsync(ct);
        var paid = await db.Payments.AsNoTracking()
            .Where(p => ids.Contains(p.CustomerId) && db.Cases.Any(k => k.Id == p.CaseId && !k.IsSample))
            .GroupBy(p => p.CustomerId)
            .Select(g => new { Id = g.Key, Sum = g.Sum(p => p.AmountRials) })
            .ToListAsync(ct);
        var visitMap = visits.ToDictionary(v => v.Id);
        var payMap = paid.ToDictionary(p => p.Id, p => p.Sum);
        return ids.ToDictionary(id => id, id =>
        {
            visitMap.TryGetValue(id, out var v);
            payMap.TryGetValue(id, out var money);
            return new CustomerStats(v?.Count ?? 0, money, v?.Last);
        });
    }

    private static object ToView(Customer c, CustomerStats? stats = null) => new
    {
        c.Id, c.Mobile, c.FullName, c.Notes, c.CreatedAt,
        VisitCount = stats?.VisitCount ?? 0,
        ValueRials = stats?.ValueRials ?? 0L,
        LastVisitAt = stats?.LastVisitAt,
        Assets = c.Assets.OrderBy(a => a.CreatedAt).Select(AssetView),
    };

    private static object AssetView(Asset a) => new
    {
        a.Id, a.CustomerId, a.Kind, a.Title, a.Identifier,
        Attributes = a.Attributes?.RootElement, a.CreatedAt,
    };

    private static JsonDocument? ToJson(Dictionary<string, string>? attributes) =>
        attributes is null or { Count: 0 } ? null : JsonSerializer.SerializeToDocument(attributes);

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Digits(string s) =>
        new(s.Select(ch => ch switch
        {
            >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
            >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
            _ => ch,
        }).Where(char.IsAsciiDigit).ToArray());

    private static string EscapeLike(string s) =>
        s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
