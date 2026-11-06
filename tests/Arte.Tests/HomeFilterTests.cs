using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>The four numbers on the home screen and the case-list filters behind them must agree.</summary>
[Collection(ApiCollection.Name)]
public sealed class HomeFilterTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> Open(HttpClient c) =>
        (await Json(await c.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "م", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        }))).GetProperty("id").GetGuid();

    private static async Task<int> Count(HttpClient c, string category) =>
        (await Json(await c.GetAsync($"/api/v1/cases?category={category}"))).GetArrayLength();

    [Fact]
    public async Task Each_open_case_is_in_exactly_one_home_bucket_and_the_filters_match_the_numbers()
    {
        var (owner, _) = await api.NewBusinessAsync();
        await Open(owner);
        await Open(owner);
        var stopped = await Open(owner);
        (await owner.PostAsJsonAsync($"/api/v1/cases/{stopped}/wait", new { waitReason = "owner_decision" })).EnsureSuccessStatusCode();

        var stats = (await Json(await owner.GetAsync("/api/v1/inbox"))).GetProperty("stats");
        var open = stats.GetProperty("open").GetInt32();
        var active = stats.GetProperty("active").GetInt32();
        var waiting = stats.GetProperty("waiting").GetInt32();
        var ready = stats.GetProperty("ready").GetInt32();

        Assert.Equal(3, open);
        Assert.Equal(open, active + waiting + ready);
        Assert.Equal(1, waiting);
        Assert.Equal(active, await Count(owner, "active"));
        Assert.Equal(waiting, await Count(owner, "waiting"));
        Assert.Equal(ready, await Count(owner, "done"));
    }
}
