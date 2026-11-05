using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>Workshop plan 9: the workshop at a glance, each person's work, the most frequent problems.</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkshopReportTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task Move(HttpClient client, Guid id, string stageName)
    {
        var detail = await Json(await client.GetAsync($"/api/v1/cases/{id}"));
        var t = detail.GetProperty("transitions").EnumerateArray().First(x => x.GetProperty("toStage").GetProperty("name").GetString() == stageName).GetProperty("id").GetGuid();
        await Json(await client.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{t}", new { }));
    }

    private static string Range()
    {
        var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("o"));
        var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("o"));
        return $"from={from}&to={to}&dayFrom={from}&dayTo={to}";
    }

    [Fact]
    public async Task Report_counts_today_each_persons_finished_jobs_and_frequent_problems()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techMobile = ArteApiFactory.NewMobile();
        var added = await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, displayName = "شاگرد", role = "technician" }));
        var (tech, _) = await api.LoginAsync(techMobile);

        Guid id = default;
        for (var i = 0; i < 2; i++)
        {
            id = (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
            {
                mobile = ArteApiFactory.NewMobile(), customerName = "م", reportedProblems = new[] { "روشن نمی‌شود" },
                newAsset = new { title = "هوندا CG 125", kind = "motorcycle" }, assigneeId = added.GetProperty("id").GetGuid(),
            }))).GetProperty("id").GetGuid();
            await Move(owner, id, "در حال تعمیر");
            await Move(tech, id, "بررسی نهایی استاد");
        }
        (await owner.PostAsJsonAsync($"/api/v1/cases/{id}/findings", new { title = "زنجیر کشیده", source = "mechanic" })).EnsureSuccessStatusCode();

        var r = await Json(await owner.GetAsync($"/api/v1/reports/workshop?{Range()}"));
        Assert.Equal(2, r.GetProperty("today").GetProperty("opened").GetInt32());
        Assert.Contains(r.GetProperty("today").GetProperty("byStatus").EnumerateArray(), s => s.GetProperty("key").GetString() == "review" && s.GetProperty("count").GetInt32() == 2);
        var person = r.GetProperty("people").EnumerateArray().Single(p => p.GetProperty("name").GetString() == "شاگرد");
        Assert.Equal(2, person.GetProperty("finished").GetInt32());
        Assert.Equal(2, r.GetProperty("reportedProblems")[0].GetProperty("count").GetInt32());
        Assert.Equal("زنجیر کشیده", r.GetProperty("foundProblems")[0].GetProperty("title").GetString());

        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync($"/api/v1/reports/workshop?{Range()}")).StatusCode);
        var (other, _) = await api.NewBusinessAsync();
        var empty = await Json(await other.GetAsync($"/api/v1/reports/workshop?{Range()}"));
        Assert.Equal(0, empty.GetProperty("today").GetProperty("opened").GetInt32());
    }
}
