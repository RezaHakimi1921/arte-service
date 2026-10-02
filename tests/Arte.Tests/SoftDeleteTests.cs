using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

[Collection(ApiCollection.Name)]
public sealed class SoftDeleteTests(ArteApiFactory api)
{
    private static async Task<Guid> Id(HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

    [Fact]
    public async Task Deleted_customer_disappears_and_comes_back_with_its_motorcycles()
    {
        var (client, _) = await api.NewBusinessAsync();
        var mobile = ArteApiFactory.NewMobile();
        var id = await Id(await client.PostAsJsonAsync("/api/v1/customers", new { mobile, fullName = "علی" }));
        await client.PostAsJsonAsync($"/api/v1/customers/{id}/assets", new { title = "Honda CG" });

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/customers/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/customers/{id}")).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers?q={mobile}")).GetArrayLength());
        var trash = await client.GetFromJsonAsync<JsonElement>("/api/v1/customers/trash");
        Assert.Contains(trash.EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);

        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/customers/{id}/restore", null)).StatusCode);
        var back = await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{id}");
        Assert.Equal(1, back.GetProperty("assets").GetArrayLength());
    }

    [Fact]
    public async Task Mobile_of_a_deleted_customer_can_be_reused_and_restore_then_conflicts()
    {
        var (client, _) = await api.NewBusinessAsync();
        var mobile = ArteApiFactory.NewMobile();
        var id = await Id(await client.PostAsJsonAsync("/api/v1/customers", new { mobile }));
        await client.DeleteAsync($"/api/v1/customers/{id}");

        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/customers", new { mobile })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync($"/api/v1/customers/{id}/restore", null)).StatusCode);
    }

    [Fact]
    public async Task Motorcycle_can_be_deleted_edited_and_restored()
    {
        var (client, _) = await api.NewBusinessAsync();
        var customer = await Id(await client.PostAsJsonAsync("/api/v1/customers", new { mobile = ArteApiFactory.NewMobile() }));
        var asset = await Id(await client.PostAsJsonAsync($"/api/v1/customers/{customer}/assets", new { title = "Yamaha" }));

        Assert.Equal(HttpStatusCode.OK, (await client.PatchAsJsonAsync($"/api/v1/assets/{asset}", new { title = "Yamaha NMAX" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/assets/{asset}")).StatusCode);
        Assert.Equal(0, (await client.GetFromJsonAsync<JsonElement>($"/api/v1/customers/{customer}")).GetProperty("assets").GetArrayLength());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync($"/api/v1/assets/{asset}/restore", null)).StatusCode);
    }

    [Fact]
    public async Task Trash_and_restore_never_cross_businesses()
    {
        var (a, _) = await api.NewBusinessAsync();
        var (b, _) = await api.NewBusinessAsync();
        var id = await Id(await a.PostAsJsonAsync("/api/v1/customers", new { mobile = ArteApiFactory.NewMobile() }));
        await a.DeleteAsync($"/api/v1/customers/{id}");

        var trash = await b.GetFromJsonAsync<JsonElement>("/api/v1/customers/trash");
        Assert.DoesNotContain(trash.EnumerateArray(), c => c.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsync($"/api/v1/customers/{id}/restore", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/v1/customers/{id}")).StatusCode);
    }

    [Fact]
    public async Task Technician_cannot_delete_customers()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var id = await Id(await owner.PostAsJsonAsync("/api/v1/customers", new { mobile = ArteApiFactory.NewMobile() }));
        var techMobile = ArteApiFactory.NewMobile();
        await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" });
        var (tech, _) = await api.LoginAsync(techMobile);

        Assert.Equal(HttpStatusCode.Forbidden, (await tech.DeleteAsync($"/api/v1/customers/{id}")).StatusCode);
    }
}
