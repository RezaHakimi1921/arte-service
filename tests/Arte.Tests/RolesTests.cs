using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>Four roles seeded from permissions: owner, senior mechanic, mechanic, apprentice (read-only).</summary>
[Collection(ApiCollection.Name)]
public sealed class RolesTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpClient> MemberAsync(HttpClient owner, string role)
    {
        var mobile = ArteApiFactory.NewMobile();
        (await owner.PostAsJsonAsync("/api/v1/staff", new { mobile, displayName = role, role })).EnsureSuccessStatusCode();
        return (await api.LoginAsync(mobile)).Item1;
    }

    private static async Task<Guid> OpenCase(HttpClient c) =>
        (await Json(await c.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "م", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        }))).GetProperty("id").GetGuid();

    private static string Report =>
        $"/api/v1/reports/summary?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-7).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}";

    private static string[] Perms(JsonElement me) =>
        me.GetProperty("business").GetProperty("permissions").EnumerateArray().Select(x => x.GetString()!).ToArray();

    [Fact]
    public async Task Each_role_gets_its_own_permissions()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var senior = Perms(await Json(await (await MemberAsync(owner, "supervisor")).GetAsync("/api/v1/me")));
        var mechanic = Perms(await Json(await (await MemberAsync(owner, "technician")).GetAsync("/api/v1/me")));
        var apprentice = Perms(await Json(await (await MemberAsync(owner, "apprentice")).GetAsync("/api/v1/me")));

        Assert.Contains("reports.view", senior);
        Assert.Contains("cases.view_all", senior);
        Assert.DoesNotContain("settings.manage", senior);
        Assert.DoesNotContain("staff.manage", senior);

        Assert.Equal(["cases.work", "cases.intake"], mechanic.OrderByDescending(x => x == "cases.work"));
        Assert.Equal(["cases.view_all"], apprentice);
    }

    [Fact]
    public async Task A_mechanic_takes_cars_in_but_cannot_reach_settings_staff_or_reports()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var mechanic = await MemberAsync(owner, "technician");
        Assert.Equal(HttpStatusCode.Created, (await mechanic.PostAsJsonAsync("/api/v1/cases", new { mobile = ArteApiFactory.NewMobile(), request = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mechanic.GetAsync(Report)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mechanic.GetAsync("/api/v1/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await mechanic.PutAsJsonAsync("/api/v1/settings/business", new { name = "x" })).StatusCode);
    }

    [Fact]
    public async Task A_senior_mechanic_sees_reports_but_not_settings()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var senior = await MemberAsync(owner, "supervisor");
        Assert.Equal(HttpStatusCode.OK, (await senior.GetAsync(Report)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await senior.PutAsJsonAsync("/api/v1/settings/business", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await senior.GetAsync("/api/v1/trash/videos")).StatusCode);
    }

    [Fact]
    public async Task An_apprentice_can_look_but_every_change_is_refused()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var caseId = await OpenCase(owner);
        var apprentice = await MemberAsync(owner, "apprentice");

        // Looking works.
        Assert.Equal(HttpStatusCode.OK, (await apprentice.GetAsync("/api/v1/cases")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await apprentice.GetAsync($"/api/v1/cases/{caseId}")).StatusCode);

        // Changing anything does not.
        Assert.Equal(HttpStatusCode.Forbidden, (await apprentice.PostAsJsonAsync("/api/v1/cases", new { mobile = ArteApiFactory.NewMobile(), request = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await apprentice.PostAsJsonAsync($"/api/v1/cases/{caseId}/notes", new { text = "سلام" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await apprentice.PatchAsJsonAsync($"/api/v1/cases/{caseId}", new { diagnosis = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await apprentice.PostAsJsonAsync($"/api/v1/cases/{caseId}/items", new { kind = "labor", title = "اجرت", quantity = 1, unitPriceRials = 1000 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await apprentice.PostAsJsonAsync($"/api/v1/cases/{caseId}/payments", new { amountRials = 1000, method = "cash" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await apprentice.PostAsJsonAsync($"/api/v1/cases/{caseId}/findings", new { title = "x", source = "mechanic" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await apprentice.PostAsJsonAsync($"/api/v1/cases/{caseId}/assign", new { assigneeId = (Guid?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await apprentice.PutAsJsonAsync("/api/v1/settings/business", new { name = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await apprentice.PostAsJsonAsync("/api/v1/staff", new { mobile = ArteApiFactory.NewMobile(), role = "technician" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await apprentice.GetAsync(Report)).StatusCode);
    }

    [Fact]
    public async Task An_unknown_role_is_refused()
    {
        var (owner, _) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = ArteApiFactory.NewMobile(), role = "boss" })).StatusCode);
    }
}
