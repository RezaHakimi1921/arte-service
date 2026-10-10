using Arte.Api.Security;
using Arte.Core.Billing;
using Arte.Core.Customers;
using Arte.Core.Data;
using Arte.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Billing;

/// <summary>
/// The shared starter list (common goods and jobs per vehicle group) and ready-made packages. Businesses read it with
/// their own remembered prices; only the platform admin edits it.
/// </summary>
public static class StandardCatalogEndpoints
{
    public sealed record StandardInput(string? Kind, string? Title, string? Category, string[]? VehicleKinds, int? SortOrder, bool? IsActive);
    public sealed record PackageLineInput(Guid StandardItemId, decimal? Quantity, bool? Optional);
    public sealed record PackageInput(string? Title, string? Description, string[]? VehicleKinds, int? SortOrder, bool? IsActive, List<PackageLineInput>? Lines);

    public static void MapStandardCatalog(this IEndpointRouteBuilder app)
    {
        // What a case can pick from, for one vehicle kind (or every kind the business takes in).
        app.MapGet("/api/v1/catalog/standard", async (string? vehicleKind, RequestUser me, ArteDbContext db, CancellationToken ct) =>
        {
            var kinds = vehicleKind is not null && AssetKinds.All.Contains(vehicleKind)
                ? [vehicleKind]
                : await db.Tenants.Where(t => t.Id == me.RequiredMembership.TenantId).Select(t => t.VehicleKinds).SingleAsync(ct);
            var showCost = me.RequiredMembership.Role == Roles.Owner;
            var prices = await db.CatalogItems.AsNoTracking().Where(c => c.StandardItemId != null && c.IsActive)
                .ToDictionaryAsync(c => c.StandardItemId!.Value, ct);
            var items = (await db.StandardItems.AsNoTracking().Where(i => i.IsActive).OrderBy(i => i.SortOrder).ToListAsync(ct))
                .Where(i => i.VehicleKinds.Intersect(kinds).Any()).ToList();
            object Line(StandardItem i) => new
            {
                i.Id, i.Kind, i.Title, i.Category,
                PriceRials = prices.TryGetValue(i.Id, out var p) ? p.DefaultPriceRials : (long?)null,
                CostRials = showCost && prices.TryGetValue(i.Id, out var pc) ? pc.DefaultCostRials : null,
            };
            var byId = items.ToDictionary(i => i.Id);
            var packages = (await db.ServicePackages.AsNoTracking().Where(p => p.IsActive).OrderBy(p => p.SortOrder).ToListAsync(ct))
                .Where(p => p.VehicleKinds.Intersect(kinds).Any()).ToList();
            var ids = packages.Select(p => p.Id).ToList();
            var lines = await db.ServicePackageLines.AsNoTracking().Where(l => ids.Contains(l.PackageId)).OrderBy(l => l.SortOrder).ToListAsync(ct);
            return Results.Ok(new
            {
                Items = items.Select(Line),
                Packages = packages.Select(p => new
                {
                    p.Id, p.Title, p.Description,
                    Lines = lines.Where(l => l.PackageId == p.Id && byId.ContainsKey(l.StandardItemId))
                        .Select(l => new { Item = Line(byId[l.StandardItemId]), l.Quantity, l.Optional }),
                }).Where(p => p.Lines.Any()),
            });
        }).RequireTenant();

        // ── platform admin ──
        var admin = app.MapGroup("/api/v1/admin/standard-catalog").RequirePlatformAdmin();
        admin.MapGet("/", async (ArteDbContext db, CancellationToken ct) => Results.Ok(new
        {
            Items = await db.StandardItems.AsNoTracking().OrderBy(i => i.SortOrder).ToListAsync(ct),
            Packages = await db.ServicePackages.AsNoTracking().OrderBy(p => p.SortOrder).ToListAsync(ct),
            Lines = await db.ServicePackageLines.AsNoTracking().OrderBy(l => l.SortOrder).ToListAsync(ct),
        }));

        admin.MapPost("/items", async (StandardInput req, ArteDbContext db, CancellationToken ct) =>
        {
            var errors = ValidateItem(req, isNew: true);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var item = new StandardItem
            {
                Kind = req.Kind!, Title = req.Title!.Trim(), Category = req.Category!.Trim(), VehicleKinds = req.VehicleKinds!.Distinct().ToArray(),
                SortOrder = req.SortOrder ?? (await db.StandardItems.MaxAsync(i => (int?)i.SortOrder, ct) ?? 0) + 1,
            };
            db.StandardItems.Add(item);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/admin/standard-catalog/items/{item.Id}", new { item.Id });
        });

        admin.MapPatch("/items/{id:guid}", async (Guid id, StandardInput req, ArteDbContext db, CancellationToken ct) =>
        {
            var errors = ValidateItem(req, isNew: false);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var item = await db.StandardItems.SingleOrDefaultAsync(i => i.Id == id, ct);
            if (item is null) return Results.NotFound();
            if (req.Kind is not null) item.Kind = req.Kind;
            if (req.Title is not null) item.Title = req.Title.Trim();
            if (req.Category is not null) item.Category = req.Category.Trim();
            if (req.VehicleKinds is not null) item.VehicleKinds = req.VehicleKinds.Distinct().ToArray();
            if (req.SortOrder is { } o) item.SortOrder = o;
            if (req.IsActive is { } a) item.IsActive = a;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        admin.MapPost("/packages", async (PackageInput req, ArteDbContext db, CancellationToken ct) =>
        {
            var errors = await ValidatePackage(req, isNew: true, db, ct);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var p = new ServicePackage
            {
                Title = req.Title!.Trim(), Description = Clean(req.Description), VehicleKinds = req.VehicleKinds!.Distinct().ToArray(),
                SortOrder = req.SortOrder ?? (await db.ServicePackages.MaxAsync(x => (int?)x.SortOrder, ct) ?? 0) + 1,
            };
            db.ServicePackages.Add(p);
            SetLines(db, p.Id, req.Lines!);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/admin/standard-catalog/packages/{p.Id}", new { p.Id });
        });

        admin.MapPatch("/packages/{id:guid}", async (Guid id, PackageInput req, ArteDbContext db, CancellationToken ct) =>
        {
            var errors = await ValidatePackage(req, isNew: false, db, ct);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var p = await db.ServicePackages.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (p is null) return Results.NotFound();
            if (req.Title is not null) p.Title = req.Title.Trim();
            if (req.Description is not null) p.Description = Clean(req.Description);
            if (req.VehicleKinds is not null) p.VehicleKinds = req.VehicleKinds.Distinct().ToArray();
            if (req.SortOrder is { } o) p.SortOrder = o;
            if (req.IsActive is { } a) p.IsActive = a;
            if (req.Lines is not null)
            {
                // Lines are only a recipe (cases copy them), so replacing them is safe.
                await db.ServicePackageLines.Where(l => l.PackageId == id).ExecuteDeleteAsync(ct);
                SetLines(db, id, req.Lines);
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void SetLines(ArteDbContext db, Guid packageId, List<PackageLineInput> lines)
    {
        var n = 0;
        foreach (var l in lines)
            db.ServicePackageLines.Add(new ServicePackageLine
            {
                PackageId = packageId, StandardItemId = l.StandardItemId, Quantity = l.Quantity ?? 1, Optional = l.Optional ?? false, SortOrder = ++n,
            });
    }

    private static Dictionary<string, string[]> ValidateItem(StandardInput req, bool isNew)
    {
        var e = new Dictionary<string, string[]>();
        if ((isNew || req.Kind is not null) && req.Kind is not (ItemKinds.Part or ItemKinds.Labor)) e["kind"] = ["نوع باید کالا یا اجرت باشد."];
        if ((isNew || req.Title is not null) && req.Title?.Trim() is not { Length: >= 2 and <= 120 }) e["title"] = ["عنوان ۲ تا ۱۲۰ حرف."];
        if ((isNew || req.Category is not null) && req.Category?.Trim() is not { Length: >= 2 and <= 40 }) e["category"] = ["گروه ۲ تا ۴۰ حرف."];
        if ((isNew || req.VehicleKinds is not null) && (req.VehicleKinds is not { Length: > 0 } || req.VehicleKinds.Any(k => !AssetKinds.All.Contains(k))))
            e["vehicleKinds"] = ["دست‌کم یک نوع وسیله."];
        return e;
    }

    private static async Task<Dictionary<string, string[]>> ValidatePackage(PackageInput req, bool isNew, ArteDbContext db, CancellationToken ct)
    {
        var e = new Dictionary<string, string[]>();
        if ((isNew || req.Title is not null) && req.Title?.Trim() is not { Length: >= 2 and <= 80 }) e["title"] = ["عنوان ۲ تا ۸۰ حرف."];
        if (req.Description is { Length: > 200 }) e["description"] = ["حداکثر ۲۰۰ حرف."];
        if ((isNew || req.VehicleKinds is not null) && (req.VehicleKinds is not { Length: > 0 } || req.VehicleKinds.Any(k => !AssetKinds.All.Contains(k))))
            e["vehicleKinds"] = ["دست‌کم یک نوع وسیله."];
        if (isNew && req.Lines is not { Count: > 0 }) e["lines"] = ["دست‌کم یک ردیف."];
        if (req.Lines is { } lines)
        {
            var ids = lines.Select(l => l.StandardItemId).ToList();
            if (lines.Count > 20 || lines.Any(l => l.Quantity is <= 0 or > 1000)) e["lines"] = ["حداکثر ۲۰ ردیف با تعداد معتبر."];
            else if (await db.StandardItems.CountAsync(i => ids.Contains(i.Id), ct) != ids.Distinct().Count()) e["lines"] = ["قلم پیدا نشد."];
        }
        return e;
    }
}
