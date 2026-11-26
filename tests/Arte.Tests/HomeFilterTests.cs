using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>Home stats and case-list filters share CaseQueue meanings (doc 33).</summary>
[Collection(ApiCollection.Name)]
public sealed class HomeFilterTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Guid Primary(JsonElement detail) =>
        detail.GetProperty("transitions").EnumerateArray().First(t => t.GetProperty("isPrimary").GetBoolean()).GetProperty("id").GetGuid();

    private static async Task<Guid> Open(HttpClient c) =>
        (await Json(await c.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "م", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        }))).GetProperty("id").GetGuid();

    private static async Task AdvanceTo(HttpClient c, Guid id, string targetKey)
    {
        for (var i = 0; i < 8; i++)
        {
            var detail = await Json(await c.GetAsync($"/api/v1/cases/{id}"));
            if (detail.GetProperty("stage").GetProperty("key").GetString() == targetKey) return;
            var primary = Primary(detail);
            detail = await Json(await c.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{primary}", new { }));
            if (detail.GetProperty("stage").GetProperty("key").GetString() == targetKey) return;
        }
        Assert.Fail($"did not reach {targetKey}");
    }

    private static async Task<int> Count(HttpClient c, string? category = null, bool all = false)
    {
        var q = all ? "all=true" : category is null ? "" : $"category={category}";
        return (await Json(await c.GetAsync($"/api/v1/cases?{q}"))).GetArrayLength();
    }

    [Fact]
    public async Task Open_includes_received_but_active_only_diagnosing_repairing_review()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var received = await Open(owner);
        var diagnosing = await Open(owner);
        await AdvanceTo(owner, diagnosing, "diagnosing");
        var waiting = await Open(owner);
        (await owner.PostAsJsonAsync($"/api/v1/cases/{waiting}/wait", new { waitReason = "owner_decision" })).EnsureSuccessStatusCode();

        var stats = (await Json(await owner.GetAsync("/api/v1/inbox"))).GetProperty("stats");
        Assert.Equal(3, stats.GetProperty("open").GetInt32());
        Assert.Equal(1, stats.GetProperty("active").GetInt32());
        Assert.Equal(1, stats.GetProperty("waiting").GetInt32());
        Assert.Equal(0, stats.GetProperty("ready").GetInt32());

        // received is open, not in active/waiting/ready — partition is not open == sum of three.
        Assert.True(stats.GetProperty("open").GetInt32() >=
            stats.GetProperty("active").GetInt32() + stats.GetProperty("waiting").GetInt32() + stats.GetProperty("ready").GetInt32());

        Assert.Equal(1, await Count(owner, "active"));
        Assert.Equal(1, await Count(owner, "waiting"));
        Assert.Equal(0, await Count(owner, "done"));
        Assert.Equal(3, await Count(owner)); // default «باز» = non-terminal
        Assert.True(await Count(owner, all: true) >= 3);

        _ = received;
    }

    [Fact]
    public async Task Delivered_and_cancelled_are_not_in_open_filter()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var deliveredId = await Open(owner);
        await AdvanceTo(owner, deliveredId, "delivered");

        var cancelledId = await Open(owner);
        var detail = await Json(await owner.GetAsync($"/api/v1/cases/{cancelledId}"));
        var cancel = detail.GetProperty("transitions").EnumerateArray()
            .First(t => t.GetProperty("toStage").GetProperty("category").GetString() == "cancelled");
        await Json(await owner.PostAsJsonAsync(
            $"/api/v1/cases/{cancelledId}/transitions/{cancel.GetProperty("id").GetGuid()}",
            new { reason = "انصراف تست" }));

        var openList = await Json(await owner.GetAsync("/api/v1/cases"));
        Assert.DoesNotContain(openList.EnumerateArray(), c => c.GetProperty("id").GetGuid() == deliveredId);
        Assert.DoesNotContain(openList.EnumerateArray(), c => c.GetProperty("id").GetGuid() == cancelledId);

        var all = await Json(await owner.GetAsync("/api/v1/cases?all=true"));
        Assert.Contains(all.EnumerateArray(), c => c.GetProperty("id").GetGuid() == deliveredId);
        Assert.Contains(all.EnumerateArray(), c => c.GetProperty("id").GetGuid() == cancelledId);
    }
}
