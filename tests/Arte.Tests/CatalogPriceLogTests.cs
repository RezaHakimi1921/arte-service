using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>The permanent buy and sell record of a catalog item: bought for 200 and sold for 300, later 300 and 400, all stays readable.</summary>
[Collection(ApiCollection.Name)]
public sealed class CatalogPriceLogTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> OpenCase(HttpClient owner) =>
        (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "مشتری", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        }))).GetProperty("id").GetGuid();

    [Fact]
    public async Task Every_purchase_and_sale_price_is_kept_in_order_and_cost_is_owner_only()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var cat = (await Json(await owner.PostAsJsonAsync("/api/v1/catalog",
            new { kind = "part", title = "لنت جلو", defaultPriceRials = 3_000_000, defaultCostRials = 2_000_000 }))).GetProperty("id").GetGuid();

        // First sale: bought 200, sold 300 (toman → rials ×10). Second time: bought 300, sold 400.
        var first = await OpenCase(owner);
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{first}/items", new { catalogItemId = cat, kind = "part", title = "لنت جلو", unitPriceRials = 3_000_000, unitCostRials = 2_000_000 }));
        var second = await OpenCase(owner);
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{second}/items", new { catalogItemId = cat, kind = "part", title = "لنت جلو", unitPriceRials = 4_000_000, unitCostRials = 3_000_000 }));
        // The default price edited by hand is logged too.
        (await owner.PatchAsJsonAsync($"/api/v1/catalog/{cat}", new { defaultPriceRials = 4_500_000 })).EnsureSuccessStatusCode();

        var history = (await Json(await owner.GetAsync($"/api/v1/catalog/{cat}/history"))).EnumerateArray().ToList();
        // catalog created, sale, sale, catalog edited: newest first.
        Assert.Equal(["catalog", "sale", "sale", "catalog"], history.Select(r => r.GetProperty("source").GetString()).ToArray());
        // Bought 300, sold 400 is the newer sale; bought 200, sold 300 is still there under it.
        Assert.Equal((4_000_000, 3_000_000), (history[1].GetProperty("priceRials").GetInt64(), history[1].GetProperty("costRials").GetInt64()));
        Assert.Equal((3_000_000, 2_000_000), (history[2].GetProperty("priceRials").GetInt64(), history[2].GetProperty("costRials").GetInt64()));
        Assert.Equal(4_500_000, history[0].GetProperty("priceRials").GetInt64());

        // A technician sees the history without purchase prices.
        var mobile = ArteApiFactory.NewMobile();
        (await owner.PostAsJsonAsync("/api/v1/staff", new { mobile, displayName = "شاگرد", role = "technician" })).EnsureSuccessStatusCode();
        var tech = (await api.LoginAsync(mobile)).Item1;
        var seen = (await Json(await tech.GetAsync($"/api/v1/catalog/{cat}/history"))).EnumerateArray().ToList();
        Assert.Equal(4, seen.Count);
        Assert.All(seen, r => Assert.Equal(JsonValueKind.Null, r.GetProperty("costRials").ValueKind));

        // Another business cannot read it.
        var (other, _) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/catalog/{cat}/history")).StatusCode);
    }
}
