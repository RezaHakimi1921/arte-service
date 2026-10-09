using System.Text.Json;
using Arte.Api.Security;
using Arte.Core.Billing;
using Arte.Core.Cases;
using Arte.Core.Common;
using Arte.Core.Customers;
using Arte.Core.Data;
using Arte.Core.Identity;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Onboarding;

/// <summary>
/// First-run experience: a fixed sample customer, motorcycle and case (mid-repair, with a part, labor and a payment)
/// that the intro tour walks through. Sample rows are flagged, removable in one go (soft delete) and never real data.
/// </summary>
public static class OnboardingEndpoints
{
    public const string SampleMobile = "09000000000";

    public static void MapOnboarding(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/onboarding").RequireTenant();

        g.MapPost("/sample", async (RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var id = await EnsureSampleAsync(db, me.RequiredMembership, clock, ct);
            return Results.Ok(new { caseId = id });
        }).RequirePermission(Permissions.CasesCreate);

        g.MapDelete("/sample", async (RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var now = clock.UtcNow;
            var cases = await db.Cases.Where(c => c.IsSample).ToListAsync(ct);
            var customers = await db.Customers.Include(c => c.Assets).Where(c => c.IsSample).ToListAsync(ct);
            foreach (var c in cases) { c.DeletedAt = now; c.DeletedBy = me.RequiredUserId; }
            foreach (var cu in customers)
            {
                cu.DeletedAt = now; cu.DeletedBy = me.RequiredUserId;
                foreach (var a in cu.Assets) { a.DeletedAt = now; a.DeletedBy = me.RequiredUserId; }
            }
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequirePermission(Permissions.CasesCreate);

        g.MapPost("/tour-done", async (RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var m = await db.Memberships.SingleAsync(x => x.Id == me.RequiredMembership.Id, ct);
            m.TourDoneAt ??= clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    /// <summary>Returns the live sample case, creating the sample set if there is none.</summary>
    public static async Task<Guid> EnsureSampleAsync(ArteDbContext db, Membership owner, IClock clock, CancellationToken ct)
    {
        var existing = await db.Cases.Where(c => c.IsSample).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
        if (existing is { } id) return id;

        var now = clock.UtcNow;
        var tenantId = owner.TenantId;
        var workflow = await db.Workflows.AsNoTracking().Include(w => w.Stages).SingleAsync(w => w.IsDefault, ct);
        var stages = workflow.Stages.Where(s => s.IsActive).OrderBy(s => s.Order).ToList();
        var first = stages.First(s => !s.IsTerminal);
        var repairing = stages.FirstOrDefault(s => s.Key == "repairing") ?? first;

        // A soft-deleted earlier sample keeps its customer row; bring it back rather than clash on the mobile.
        var customer = await db.Customers.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).Include(c => c.Assets)
            .SingleOrDefaultAsync(c => c.Mobile == SampleMobile, ct);
        if (customer is null)
        {
            customer = new Customer { TenantId = tenantId, Mobile = SampleMobile, FullName = "مشتری نمونه", CreatedAt = now, UpdatedAt = now };
            db.Customers.Add(customer);
        }
        customer.IsSample = true;
        customer.DeletedAt = null;
        customer.DeletedBy = null;
        customer.Notes = "این مشتری برای آشنایی با برنامه ساخته شده است.";

        // The sample vehicle matches the shop: a motorcycle for motorcycle repair, otherwise a car.
        var kinds = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.VehicleKinds).SingleAsync(ct);
        var moto = kinds.Contains(AssetKinds.Motorcycle) && !kinds.Contains(AssetKinds.Car);
        var asset = new Asset
        {
            TenantId = tenantId, CustomerId = customer.Id, Kind = moto ? AssetKinds.Motorcycle : AssetKinds.Car,
            Title = moto ? "هوندا CG 125" : "پژو ۲۰۶", Identifier = moto ? "123-45678" : "12ب345-11", CreatedAt = now,
            Attributes = JsonSerializer.SerializeToDocument(new Dictionary<string, string> { ["year"] = "1402", ["color"] = "قرمز" }),
        };
        db.Assets.Add(asset);

        var number = (await db.Database
            .SqlQuery<long>($"UPDATE \"Tenants\" SET \"LastCaseNumber\" = \"LastCaseNumber\" + 1 WHERE \"Id\" = {tenantId} RETURNING \"LastCaseNumber\" AS \"Value\"")
            .ToListAsync(ct)).Single();

        var opened = now.AddHours(-3);
        var c = new Case
        {
            TenantId = tenantId, Number = number, CustomerId = customer.Id, AssetId = asset.Id, WorkflowId = workflow.Id,
            StageId = repairing.Id, AssigneeId = owner.Id, IsSample = true, TrackingCode = Arte.Api.Tracking.TrackingCodes.New(),
            Request = moto ? "موقع گاز دادن صدا می‌دهد و زنجیر شل شده است." : "موقع ترمز صدا می‌دهد و وقت تعویض روغن است.",
            ReportedProblems = moto ? ["صدای غیرعادی", "زنجیر صدا می‌دهد"] : ["صدای غیرعادی"],
            RequestedServices = moto ? ["تعویض روغن", "تنظیم و روغن‌کاری زنجیر"] : ["تعویض روغن", "تعویض لنت جلو"],
            Diagnosis = moto ? "زنجیر کشیده شده و روغن موتور کم است." : "لنت جلو تمام شده و روغن موتور کم است.",
            OdometerKm = 12_400, FuelLevel = 2, BodyStatus = "ok",
            // Tomorrow 14:00 Tehran (10:30 UTC); built in UTC because Postgres stores only UTC offsets.
            PromisedAt = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(1).AddHours(10.5),
            OpenedAt = opened, StageEnteredAt = now.AddHours(-1), OpenedBy = owner.UserId,
        };
        db.Cases.Add(c);

        db.CaseItems.AddRange(
            new CaseItem
            {
                TenantId = tenantId, CaseId = c.Id, Kind = ItemKinds.Part, Title = moto ? "روغن موتور ۱ لیتری" : "روغن موتور ۴ لیتری",
                UnitCostRials = 3_000_000, UnitPriceRials = 3_800_000, WarrantyDays = 0, AddedBy = owner.UserId, AddedAt = now, UpdatedAt = now,
            },
            new CaseItem
            {
                TenantId = tenantId, CaseId = c.Id, Kind = ItemKinds.Labor, Title = moto ? "تعویض روغن و تنظیم زنجیر" : "تعویض روغن و لنت جلو",
                UnitPriceRials = 2_500_000, PerformedBy = owner.Id, AddedBy = owner.UserId, AddedAt = now, UpdatedAt = now,
            });
        db.Payments.Add(new Payment
        {
            TenantId = tenantId, CaseId = c.Id, CustomerId = customer.Id, AmountRials = 2_000_000, Method = PaymentMethods.Card,
            PaidAt = now.AddHours(-2), RecordedBy = owner.UserId, Note = "بیعانه",
        });

        void Event(string type, DateTimeOffset at, object data) => db.CaseEvents.Add(new CaseEvent
        {
            TenantId = tenantId, CaseId = c.Id, Type = type, ActorUserId = owner.UserId, OccurredAt = at,
            Data = JsonSerializer.SerializeToDocument(data, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        });
        Event(CaseEventTypes.Opened, opened, new { c.Number, Stage = first.Name });
        Event(CaseEventTypes.StageChanged, now.AddHours(-1), new { From = first.Name, To = repairing.Name, Action = "شروع تعمیر" });
        await db.SaveChangesAsync(ct);
        return c.Id;
    }
}
