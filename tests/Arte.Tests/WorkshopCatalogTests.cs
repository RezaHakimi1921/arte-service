using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>Workshop plan 1B: editable problem/service lists, intake checklist per motor type, problems recorded on a case.</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkshopCatalogTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpClient> TechAsync(HttpClient owner)
    {
        var mobile = ArteApiFactory.NewMobile();
        (await owner.PostAsJsonAsync("/api/v1/staff", new { mobile, displayName = "شاگرد", role = "technician" })).EnsureSuccessStatusCode();
        return (await api.LoginAsync(mobile)).Item1;
    }

    private static async Task<Guid> OpenCase(HttpClient owner, object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["mobile"] = ArteApiFactory.NewMobile(), ["customerName"] = "مشتری", ["reportedProblems"] = new[] { "روشن نمی‌شود" },
            ["newAsset"] = new { title = "هوندا CG 125", kind = "motorcycle" },
        };
        if (extra is not null)
            foreach (var p in extra.GetType().GetProperties()) body[char.ToLowerInvariant(p.Name[0]) + p.Name[1..]] = p.GetValue(extra);
        return (await Json(await owner.PostAsJsonAsync("/api/v1/cases", body))).GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task Manager_edits_the_problem_list_and_staff_only_see_active_rows()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var created = await Json(await owner.PostAsJsonAsync("/api/v1/intake-options", new { kind = "problem", group = "موتور", title = "صدای غیرعادی" }));
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/v1/intake-options", new { kind = "problem", title = "صدای غیرعادی" })).StatusCode);

        var tech = await TechAsync(owner);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync("/api/v1/intake-options", new { kind = "problem", title = "x" })).StatusCode);
        Assert.Single((await Json(await tech.GetAsync("/api/v1/intake-options?kind=problem"))).EnumerateArray());

        (await owner.PatchAsJsonAsync($"/api/v1/intake-options/{id}", new { isActive = false })).EnsureSuccessStatusCode();
        Assert.Empty((await Json(await tech.GetAsync("/api/v1/intake-options?kind=problem"))).EnumerateArray());
        Assert.Single((await Json(await owner.GetAsync("/api/v1/intake-options?kind=problem&includeInactive=true"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync("/api/v1/intake-options?includeInactive=true")).StatusCode);
    }

    [Fact]
    public async Task Another_business_never_sees_or_edits_our_options()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (other, _) = await api.NewBusinessAsync();
        var id = (await Json(await owner.PostAsJsonAsync("/api/v1/intake-options", new { kind = "service", title = "سرویس ویژه" }))).GetProperty("id").GetGuid();

        Assert.Empty((await Json(await other.GetAsync("/api/v1/intake-options"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.PatchAsJsonAsync($"/api/v1/intake-options/{id}", new { title = "x" })).StatusCode);
    }

    [Fact]
    public async Task Default_checklist_fills_only_an_empty_list_and_can_be_reordered()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var added = await Json(await owner.PostAsync("/api/v1/checklist-templates/geared/defaults", null));
        Assert.True(added.GetProperty("added").GetInt32() >= 20);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsync("/api/v1/checklist-templates/geared/defaults", null)).StatusCode);

        var rows = (await Json(await owner.GetAsync("/api/v1/checklist-templates/geared"))).EnumerateArray().ToList();
        var ids = rows.Select(r => r.GetProperty("id").GetGuid()).Reverse().ToArray();
        (await owner.PutAsJsonAsync("/api/v1/checklist-templates/geared/order", new { ids })).EnsureSuccessStatusCode();
        var after = (await Json(await owner.GetAsync("/api/v1/checklist-templates/geared"))).EnumerateArray().ToList();
        Assert.Equal(ids[0], after[0].GetProperty("id").GetGuid());

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/v1/checklist-templates/unknown")).StatusCode);
    }

    [Fact]
    public async Task Required_checklist_rows_must_be_ticked_before_the_case_opens_and_are_saved_with_it()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var a = (await Json(await owner.PostAsJsonAsync("/api/v1/checklist-templates/automatic/items", new { title = "بررسی ترمز" }))).GetProperty("id").GetGuid();
        var b = (await Json(await owner.PostAsJsonAsync("/api/v1/checklist-templates/automatic/items", new { title = "بررسی چراغ", isRequired = false }))).GetProperty("id").GetGuid();

        var bad = await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "م", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = "کلیک", kind = "motorcycle" }, motorType = "automatic", checklist = new object[0],
        });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("بررسی ترمز", await bad.Content.ReadAsStringAsync());

        var id = await OpenCase(owner, new { MotorType = "automatic", Checklist = new[] { new { itemId = a, isChecked = true, note = "ok" } } });
        var detail = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        Assert.Equal("automatic", detail.GetProperty("motorType").GetString());
        var list = detail.GetProperty("checklist").EnumerateArray().ToList();
        Assert.Equal(2, list.Count);
        Assert.True(list[0].GetProperty("isChecked").GetBoolean());

        // The rest of the list can be ticked later, and it is on the timeline.
        var rowId = list[1].GetProperty("id").GetGuid();
        (await owner.PutAsJsonAsync($"/api/v1/cases/{id}/checklist/{rowId}", new { isChecked = true })).EnsureSuccessStatusCode();
        var timeline = (await Json(await owner.GetAsync($"/api/v1/cases/{id}"))).GetProperty("timeline").EnumerateArray();
        Assert.Contains(timeline, e => e.GetProperty("type").GetString() == "case.checklist_changed");
        _ = b;
    }

    [Fact]
    public async Task Findings_can_be_added_removed_and_restored_and_other_business_gets_404()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (other, _) = await api.NewBusinessAsync();
        var id = await OpenCase(owner);

        var fid = (await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/findings", new { title = "زنجیر کشیده", source = "mechanic" }))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/cases/{id}/findings", new { title = "", source = "mechanic" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/cases/{id}/findings", new { title = "x" })).StatusCode);

        Assert.Single((await Json(await owner.GetAsync($"/api/v1/cases/{id}"))).GetProperty("findings").EnumerateArray());
        (await owner.DeleteAsync($"/api/v1/cases/{id}/findings/{fid}")).EnsureSuccessStatusCode();
        Assert.Empty((await Json(await owner.GetAsync($"/api/v1/cases/{id}"))).GetProperty("findings").EnumerateArray());
        (await owner.PostAsync($"/api/v1/cases/{id}/findings/{fid}/restore", null)).EnsureSuccessStatusCode();
        Assert.Single((await Json(await owner.GetAsync($"/api/v1/cases/{id}"))).GetProperty("findings").EnumerateArray());
    }
}
