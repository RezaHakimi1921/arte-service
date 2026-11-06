using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>Checklist categories (a managed list), drag-and-drop order of the option lists, and the admin-only business type.</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkshopCategoryTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> AddCategory(HttpClient owner, string title) =>
        (await Json(await owner.PostAsJsonAsync("/api/v1/intake-options", new { kind = "category", title }))).GetProperty("id").GetGuid();

    [Fact]
    public async Task Categories_are_listed_reordered_and_a_rename_follows_the_checklist_rows()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var a = await AddCategory(owner, "موتور");
        var b = await AddCategory(owner, "ترمز");

        (await owner.PutAsJsonAsync("/api/v1/intake-options/order", new { ids = new[] { b, a } })).EnsureSuccessStatusCode();
        var list = (await Json(await owner.GetAsync("/api/v1/intake-options?kind=category"))).EnumerateArray().Select(x => x.GetProperty("title").GetString()).ToArray();
        Assert.Equal(["ترمز", "موتور"], list);

        await Json(await owner.PostAsJsonAsync("/api/v1/checklist-templates/geared/items", new { title = "لنت جلو", group = "ترمز" }));
        (await owner.PatchAsJsonAsync($"/api/v1/intake-options/{b}", new { title = "ترمز و چرخ" })).EnsureSuccessStatusCode();
        var rows = (await Json(await owner.GetAsync("/api/v1/checklist-templates/geared"))).EnumerateArray().ToList();
        Assert.Equal("ترمز و چرخ", rows.Single(r => r.GetProperty("title").GetString() == "لنت جلو").GetProperty("group").GetString());

        // Ids from a missing row, or from two different lists, are refused.
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync("/api/v1/intake-options/order", new { ids = new[] { a, Guid.NewGuid() } })).StatusCode);
        var service = (await Json(await owner.PostAsJsonAsync("/api/v1/intake-options", new { kind = "service", title = "تعویض روغن" }))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PutAsJsonAsync("/api/v1/intake-options/order", new { ids = new[] { a, service } })).StatusCode);
    }

    [Fact]
    public async Task Order_needs_the_settings_permission_and_other_businesses_get_404()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var a = await AddCategory(owner, "برق");
        var mobile = ArteApiFactory.NewMobile();
        (await owner.PostAsJsonAsync("/api/v1/staff", new { mobile, displayName = "شاگرد", role = "technician" })).EnsureSuccessStatusCode();
        var tech = (await api.LoginAsync(mobile)).Item1;
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PutAsJsonAsync("/api/v1/intake-options/order", new { ids = new[] { a } })).StatusCode);

        var (other, _) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync("/api/v1/intake-options/order", new { ids = new[] { a } })).StatusCode);
    }

    [Fact]
    public async Task Business_type_and_vehicle_kinds_cannot_be_changed_by_the_owner()
    {
        var (owner, _) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync("/api/v1/settings/business", new { businessType = "car_repair" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync("/api/v1/settings/business", new { vehicleKinds = new[] { "car" } })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PutAsJsonAsync("/api/v1/settings/business", new { instagramId = "my_shop" })).StatusCode);
    }

    [Fact]
    public async Task Service_categories_are_their_own_list_and_a_rename_follows_the_services()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var cat = (await Json(await owner.PostAsJsonAsync("/api/v1/intake-options", new { kind = "service_category", title = "ترمز" }))).GetProperty("id").GetGuid();
        await Json(await owner.PostAsJsonAsync("/api/v1/intake-options", new { kind = "category", title = "فقط چک‌لیست" }));
        await Json(await owner.PostAsJsonAsync("/api/v1/intake-options", new { kind = "service", title = "تعویض لنت", group = "ترمز" }));

        var cats = (await Json(await owner.GetAsync("/api/v1/intake-options?kind=service_category"))).EnumerateArray().Select(x => x.GetProperty("title").GetString()).ToArray();
        Assert.Equal(["ترمز"], cats);

        (await owner.PatchAsJsonAsync($"/api/v1/intake-options/{cat}", new { title = "ترمز و چرخ" })).EnsureSuccessStatusCode();
        var services = (await Json(await owner.GetAsync("/api/v1/intake-options?kind=service"))).EnumerateArray().ToList();
        Assert.Equal("ترمز و چرخ", services.Single().GetProperty("group").GetString());
    }
}
