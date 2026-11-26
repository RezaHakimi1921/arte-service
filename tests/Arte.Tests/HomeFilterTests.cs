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

    private static async Task<JsonElement> Detail(HttpClient c, Guid id) =>
        await Json(await c.GetAsync($"/api/v1/cases/{id}"));

    private static async Task<JsonElement> Run(HttpClient c, Guid id, Guid transitionId, object? body = null) =>
        await Json(await c.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{transitionId}", body ?? new { }));

    private static async Task AdvanceTo(HttpClient c, Guid id, string targetKey)
    {
        // Transitions expose toStage.name/category, not key — walk with known path + labels.
        for (var i = 0; i < 12; i++)
        {
            var detail = await Detail(c, id);
            var key = detail.GetProperty("stage").GetProperty("key").GetString();
            if (key == targetKey) return;

            if (targetKey == "awaiting_parts")
            {
                var parts = detail.GetProperty("transitions").EnumerateArray()
                    .FirstOrDefault(t => t.GetProperty("label").GetString()!.Contains("منتظر قطعه"));
                if (parts.ValueKind != JsonValueKind.Undefined)
                {
                    await Run(c, id, parts.GetProperty("id").GetGuid(), new { waitReason = "shop_parts" });
                    continue;
                }
            }

            if (targetKey == "repairing" && key is "received" or "diagnosing")
            {
                var startRepair = detail.GetProperty("transitions").EnumerateArray()
                    .FirstOrDefault(t => t.GetProperty("label").GetString() == "شروع تعمیر");
                if (startRepair.ValueKind != JsonValueKind.Undefined)
                {
                    await Run(c, id, startRepair.GetProperty("id").GetGuid());
                    continue;
                }
            }

            await Run(c, id, Primary(detail));
        }
        Assert.Fail($"did not reach {targetKey}; last={(await Detail(c, id)).GetProperty("stage").GetProperty("key").GetString()}");
    }

    private static async Task<int> Count(HttpClient c, string? category = null, bool all = false)
    {
        var q = all ? "all=true" : category is null ? "" : $"category={category}";
        return (await Json(await c.GetAsync($"/api/v1/cases?{q}"))).GetArrayLength();
    }

    private static async Task AssertStatsMatchFilters(HttpClient owner)
    {
        var stats = (await Json(await owner.GetAsync("/api/v1/inbox"))).GetProperty("stats");
        Assert.Equal(stats.GetProperty("active").GetInt32(), await Count(owner, "active"));
        Assert.Equal(stats.GetProperty("waiting").GetInt32(), await Count(owner, "waiting"));
        Assert.Equal(stats.GetProperty("ready").GetInt32(), await Count(owner, "done"));
        Assert.Equal(stats.GetProperty("open").GetInt32(), await Count(owner));
    }

    [Fact]
    public async Task In_progress_keeps_diagnosing_even_with_wait_reason_waiting_is_stage_category()
    {
        var (owner, _) = await api.NewBusinessAsync();

        await Open(owner); // received — open only

        var diagnosing = await Open(owner);
        await AdvanceTo(owner, diagnosing, "diagnosing");
        (await owner.PostAsJsonAsync($"/api/v1/cases/{diagnosing}/wait", new { waitReason = "owner_decision" })).EnsureSuccessStatusCode();
        Assert.Equal("diagnosing", (await Detail(owner, diagnosing)).GetProperty("stage").GetProperty("key").GetString());
        Assert.Equal("owner_decision", (await Detail(owner, diagnosing)).GetProperty("waitReason").GetString());

        var awaitingParts = await Open(owner);
        await AdvanceTo(owner, awaitingParts, "awaiting_parts");
        Assert.Equal("awaiting_parts", (await Detail(owner, awaitingParts)).GetProperty("stage").GetProperty("key").GetString());
        Assert.Equal("waiting", (await Detail(owner, awaitingParts)).GetProperty("stage").GetProperty("category").GetString());

        var stats = (await Json(await owner.GetAsync("/api/v1/inbox"))).GetProperty("stats");
        Assert.Equal(3, stats.GetProperty("open").GetInt32());
        Assert.Equal(1, stats.GetProperty("active").GetInt32()); // diagnosing + waitReason still in progress
        Assert.Equal(1, stats.GetProperty("waiting").GetInt32()); // awaiting_parts only
        Assert.Equal(0, stats.GetProperty("ready").GetInt32());
        Assert.True(stats.GetProperty("blocked").GetInt32() >= 1);

        await AssertStatsMatchFilters(owner);
    }

    [Fact]
    public async Task Repairing_and_review_are_in_progress_ready_is_not()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var repairing = await Open(owner);
        await AdvanceTo(owner, repairing, "repairing");
        Assert.Equal("repairing", (await Detail(owner, repairing)).GetProperty("stage").GetProperty("key").GetString());

        var review = await Open(owner);
        await AdvanceTo(owner, review, "review");
        Assert.Equal("review", (await Detail(owner, review)).GetProperty("stage").GetProperty("key").GetString());

        var ready = await Open(owner);
        await AdvanceTo(owner, ready, "ready");
        Assert.Equal("ready", (await Detail(owner, ready)).GetProperty("stage").GetProperty("key").GetString());

        var stats = (await Json(await owner.GetAsync("/api/v1/inbox"))).GetProperty("stats");
        Assert.Equal(2, stats.GetProperty("active").GetInt32());
        Assert.Equal(1, stats.GetProperty("ready").GetInt32());
        Assert.Equal(3, stats.GetProperty("open").GetInt32());
        await AssertStatsMatchFilters(owner);
    }

    [Fact]
    public async Task Delivered_and_cancelled_are_not_in_open_filter()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var deliveredId = await Open(owner);
        await AdvanceTo(owner, deliveredId, "delivered");
        Assert.Equal("delivered", (await Detail(owner, deliveredId)).GetProperty("stage").GetProperty("key").GetString());

        var cancelledId = await Open(owner);
        var detail = await Detail(owner, cancelledId);
        var cancel = detail.GetProperty("transitions").EnumerateArray()
            .First(t => t.GetProperty("toStage").GetProperty("category").GetString() == "cancelled");
        await Run(owner, cancelledId, cancel.GetProperty("id").GetGuid(), new { reason = "انصراف تست" });

        var openList = await Json(await owner.GetAsync("/api/v1/cases"));
        Assert.DoesNotContain(openList.EnumerateArray(), c => c.GetProperty("id").GetGuid() == deliveredId);
        Assert.DoesNotContain(openList.EnumerateArray(), c => c.GetProperty("id").GetGuid() == cancelledId);

        var all = await Json(await owner.GetAsync("/api/v1/cases?all=true"));
        Assert.Contains(all.EnumerateArray(), c => c.GetProperty("id").GetGuid() == deliveredId);
        Assert.Contains(all.EnumerateArray(), c => c.GetProperty("id").GetGuid() == cancelledId);
        Assert.Equal((await Json(await owner.GetAsync("/api/v1/inbox"))).GetProperty("stats").GetProperty("open").GetInt32(),
            await Count(owner));
    }
}
