using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

[Collection(ApiCollection.Name)]
public sealed class CustomerDirectoryTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Customer_list_shows_vehicle_visits_and_payments()
    {
        var (owner, tenant) = await api.NewBusinessAsync();
        var mobile = ArteApiFactory.NewMobile();
        var created = await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile,
            customerName = "علی رضایی",
            newAsset = new { title = "هوندا CG", identifier = "123-45678" },
            request = "روغن",
        }));
        var caseId = created.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync($"/api/v1/cases/{caseId}/payments", new { amountRials = 250_000, method = "cash" })).StatusCode);

        var list = await Json(await owner.GetAsync($"/api/v1/customers?q={mobile}"));
        var row = list.EnumerateArray().Single();
        Assert.Equal("هوندا CG", row.GetProperty("vehicleTitle").GetString());
        Assert.Equal(1, row.GetProperty("visitCount").GetInt32());
        Assert.Equal(250_000, row.GetProperty("valueRials").GetInt64());

        var id = row.GetProperty("id").GetGuid();
        var view = await Json(await owner.GetAsync($"/api/v1/customers/{id}"));
        Assert.Equal(1, view.GetProperty("visitCount").GetInt32());
        Assert.Equal(250_000, view.GetProperty("valueRials").GetInt64());
        Assert.NotEqual(JsonValueKind.Null, view.GetProperty("lastVisitAt").ValueKind);

        var (other, _) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/customers/{id}")).StatusCode);

        var techMobile = ArteApiFactory.NewMobile();
        (await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" })).EnsureSuccessStatusCode();
        var (tech, _) = await api.LoginAsync(techMobile);
        await ArteApiFactory.SelectAsync(tech, tenant);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync("/api/v1/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync($"/api/v1/customers/{id}")).StatusCode);
    }

    [Fact]
    public async Task Customer_mobile_must_be_09_and_eleven_digits_and_cannot_change()
    {
        var (owner, _) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/customers", new { mobile = "02123456789", fullName = "علی" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/customers", new { mobile = "9123456789", fullName = "علی" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/customers", new { mobile = "0912123456", fullName = "علی" })).StatusCode);

        var mobile = ArteApiFactory.NewMobile();
        var created = await Json(await owner.PostAsJsonAsync("/api/v1/customers", new { mobile, fullName = "علی رضایی" }));
        var id = created.GetProperty("id").GetGuid();

        var changed = await owner.PatchAsJsonAsync($"/api/v1/customers/{id}", new { mobile = ArteApiFactory.NewMobile(), fullName = "حسن رضایی" });
        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);

        var renamed = await Json(await owner.PatchAsJsonAsync($"/api/v1/customers/{id}", new { fullName = "حسن رضایی", notes = "یادداشت" }));
        Assert.Equal(mobile, renamed.GetProperty("mobile").GetString());
        Assert.Equal("حسن رضایی", renamed.GetProperty("fullName").GetString());
    }
}
