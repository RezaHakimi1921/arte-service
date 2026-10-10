using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arte.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arte.Tests;

/// <summary>Shared starter list of goods and jobs per vehicle group, ready-made packages, remembered prices.</summary>
[Collection(ApiCollection.Name)]
public sealed class StandardCatalogTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return res.StatusCode == HttpStatusCode.NoContent ? default : await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> OpenCase(HttpClient owner, string kind = "motorcycle") =>
        (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "علی", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = kind == "motorcycle" ? "هوندا CG 125" : "پژو 206", kind },
        }))).GetProperty("id").GetGuid();

    private static JsonElement Item(JsonElement list, string title) =>
        list.GetProperty("items").EnumerateArray().First(i => i.GetProperty("title").GetString() == title);

    [Fact]
    public async Task Each_vehicle_kind_gets_its_own_list_and_packages()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var moto = await Json(await owner.GetAsync("/api/v1/catalog/standard?vehicleKind=motorcycle"));
        var titles = moto.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("title").GetString()).ToList();
        Assert.Contains("زنجیر", titles);
        Assert.DoesNotContain("تسمه تایم", titles);
        Assert.Contains(moto.GetProperty("packages").EnumerateArray(), p => p.GetProperty("title").GetString() == "تعویض کیت زنجیر");
        // No prices until the business uses an item.
        Assert.Equal(JsonValueKind.Null, Item(moto, "زنجیر").GetProperty("priceRials").ValueKind);

        var car = await Json(await owner.GetAsync("/api/v1/catalog/standard?vehicleKind=car"));
        Assert.Contains(car.GetProperty("items").EnumerateArray(), i => i.GetProperty("title").GetString() == "تسمه تایم");
        Assert.DoesNotContain(car.GetProperty("items").EnumerateArray(), i => i.GetProperty("title").GetString() == "زنجیر");
    }

    [Fact]
    public async Task A_package_adds_its_lines_at_once_and_the_prices_are_remembered_for_this_business_only()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var id = await OpenCase(owner);
        var list = await Json(await owner.GetAsync("/api/v1/catalog/standard?vehicleKind=motorcycle"));
        var oil = list.GetProperty("packages").EnumerateArray().First(p => p.GetProperty("title").GetString() == "تعویض روغن");
        var lines = oil.GetProperty("lines").EnumerateArray().Where(l => !l.GetProperty("optional").GetBoolean()).ToList();
        Assert.Equal(2, lines.Count);   // oil + labor; the filter is optional

        var prices = new long[] { 4_500_000, 1_500_000 };
        var body = new
        {
            package = "تعویض روغن",
            lines = lines.Select((l, i) => new
            {
                standardItemId = l.GetProperty("item").GetProperty("id").GetGuid(),
                quantity = l.GetProperty("quantity").GetDecimal(),
                unitPriceRials = prices[i],
            }).ToArray(),
        };
        var billing = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items/batch", body));
        Assert.Equal(2, billing.GetProperty("items").GetArrayLength());
        Assert.Equal(6_000_000, billing.GetProperty("money").GetProperty("totalRials").GetInt64());
        // The work line counts for the person who recorded it.
        var labor = billing.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "labor");
        Assert.NotEqual(JsonValueKind.Null, labor.GetProperty("performedBy").ValueKind);

        list = await Json(await owner.GetAsync("/api/v1/catalog/standard?vehicleKind=motorcycle"));
        Assert.Equal(4_500_000, Item(list, "روغن موتور").GetProperty("priceRials").GetInt64());

        // The last price used wins; a single line works the same way.
        var oilId = Item(list, "روغن موتور").GetProperty("id").GetGuid();
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { standardItemId = oilId, unitPriceRials = 4_800_000 }));
        list = await Json(await owner.GetAsync("/api/v1/catalog/standard?vehicleKind=motorcycle"));
        Assert.Equal(4_800_000, Item(list, "روغن موتور").GetProperty("priceRials").GetInt64());
        // And it is one row in the business's price list, not a new one each time.
        var catalog = await Json(await owner.GetAsync("/api/v1/catalog?q=روغن موتور"));
        Assert.Single(catalog.EnumerateArray());

        // Another business does not see these prices.
        var (other, _) = await api.NewBusinessAsync();
        var theirs = await Json(await other.GetAsync("/api/v1/catalog/standard?vehicleKind=motorcycle"));
        Assert.Equal(JsonValueKind.Null, Item(theirs, "روغن موتور").GetProperty("priceRials").ValueKind);

        // Bad input adds nothing.
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items/batch", new { lines = Array.Empty<object>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items/batch",
            new { lines = new object[] { new { standardItemId = oilId, unitPriceRials = 1 }, new { standardItemId = Guid.NewGuid() } } })).StatusCode);
        Assert.Equal(3, (await Json(await owner.GetAsync($"/api/v1/cases/{id}"))).GetProperty("billing").GetProperty("items").GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/cases/{id}/items/batch", body)).StatusCode);
    }

    [Fact]
    public async Task Best_sellers_report_groups_goods_and_work_by_name()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var list = await Json(await owner.GetAsync("/api/v1/catalog/standard?vehicleKind=motorcycle"));
        var oilId = Item(list, "روغن موتور").GetProperty("id").GetGuid();
        var from = DateTimeOffset.UtcNow.AddMinutes(-1);
        foreach (var _ in Enumerable.Range(0, 2))
        {
            var id = await OpenCase(owner);
            await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { standardItemId = oilId, unitPriceRials = 4_000_000, unitCostRials = 3_000_000 }));
            var c = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
            while (c.GetProperty("stage").GetProperty("key").GetString() != "delivered")
            {
                var t = c.GetProperty("transitions").EnumerateArray().First(x => x.GetProperty("isPrimary").GetBoolean()).GetProperty("id");
                c = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{t}", new { allowCredit = true }));
            }
        }
        var r = await Json(await owner.GetAsync($"/api/v1/reports/summary?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"))}"));
        var top = r.GetProperty("topItems").EnumerateArray().First();
        Assert.Equal("روغن موتور", top.GetProperty("title").GetString());
        Assert.Equal(2, top.GetProperty("cases").GetInt32());
        Assert.Equal(8_000_000, top.GetProperty("salesRials").GetInt64());
        Assert.Equal(2_000_000, top.GetProperty("profitRials").GetInt64());
    }

    [Fact]
    public async Task Only_the_platform_admin_edits_the_starter_list()
    {
        var (owner, _) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/admin/standard-catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/v1/admin/standard-catalog/items",
            new { kind = "part", title = "کیت تست", category = "تست", vehicleKinds = new[] { "motorcycle" } })).StatusCode);

        var mobile = ArteApiFactory.NewMobile();
        var (admin, _) = await api.LoginAsync(mobile);
        await using (var scope = api.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ArteDbContext>().Users.Where(u => u.Mobile == mobile)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsPlatformAdmin, true));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/admin/standard-catalog/items",
            new { kind = "service", title = "x", category = "", vehicleKinds = Array.Empty<string>() })).StatusCode);
        var title = $"لنت سرامیکی {Guid.NewGuid():N}"[..20];
        var itemId = (await Json(await admin.PostAsJsonAsync("/api/v1/admin/standard-catalog/items",
            new { kind = "part", title, category = "ترمز", vehicleKinds = new[] { "motorcycle" } }))).GetProperty("id").GetGuid();
        var pkgId = (await Json(await admin.PostAsJsonAsync("/api/v1/admin/standard-catalog/packages",
            new { title = "بسته تست", vehicleKinds = new[] { "motorcycle" }, lines = new[] { new { standardItemId = itemId, quantity = 2m, optional = false } } }))).GetProperty("id").GetGuid();

        var list = await Json(await owner.GetAsync("/api/v1/catalog/standard?vehicleKind=motorcycle"));
        Assert.Contains(list.GetProperty("items").EnumerateArray(), i => i.GetProperty("title").GetString() == title);
        var pkg = list.GetProperty("packages").EnumerateArray().Single(p => p.GetProperty("id").GetGuid() == pkgId);
        Assert.Equal(2m, pkg.GetProperty("lines")[0].GetProperty("quantity").GetDecimal());

        // Switched off, not deleted: it leaves the pickers.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/v1/admin/standard-catalog/items/{itemId}", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/v1/admin/standard-catalog/packages/{pkgId}", new { isActive = false })).StatusCode);
        list = await Json(await owner.GetAsync("/api/v1/catalog/standard?vehicleKind=motorcycle"));
        Assert.DoesNotContain(list.GetProperty("items").EnumerateArray(), i => i.GetProperty("title").GetString() == title);
        Assert.DoesNotContain(list.GetProperty("packages").EnumerateArray(), p => p.GetProperty("id").GetGuid() == pkgId);
    }
}
