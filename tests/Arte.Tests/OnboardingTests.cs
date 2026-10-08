using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

[Collection(ApiCollection.Name)]
public sealed class OnboardingTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Sample_case_is_created_once_shown_in_me_removable_and_recreatable()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var me = await owner.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.False(me.GetProperty("business").GetProperty("tourDone").GetBoolean());
        Assert.Equal(JsonValueKind.Null, me.GetProperty("business").GetProperty("sampleCaseId").ValueKind);

        var id = (await Json(await owner.PostAsync("/api/v1/onboarding/sample", null))).GetProperty("caseId").GetGuid();
        var again = (await Json(await owner.PostAsync("/api/v1/onboarding/sample", null))).GetProperty("caseId").GetGuid();
        Assert.Equal(id, again);

        var c = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        Assert.Equal("repairing", c.GetProperty("stage").GetProperty("key").GetString());
        Assert.Equal(2, c.GetProperty("billing").GetProperty("items").GetArrayLength());
        Assert.Equal("هوندا CG 125", c.GetProperty("asset").GetProperty("title").GetString());
        me = await owner.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal(id, me.GetProperty("business").GetProperty("sampleCaseId").GetGuid());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/v1/onboarding/sample")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/cases/{id}")).StatusCode);

        // Asking again brings a fresh sample back (the soft-deleted customer is reused, not duplicated).
        var fresh = (await Json(await owner.PostAsync("/api/v1/onboarding/sample", null))).GetProperty("caseId").GetGuid();
        Assert.NotEqual(id, fresh);
    }

    [Fact]
    public async Task Tour_done_is_remembered_per_member()
    {
        var (owner, _) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/api/v1/onboarding/tour-done", null)).StatusCode);
        var me = await owner.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.True(me.GetProperty("business").GetProperty("tourDone").GetBoolean());
    }

    [Fact]
    public async Task Technician_cannot_create_sample_and_other_business_cannot_see_it()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (other, _) = await api.NewBusinessAsync();
        var id = (await Json(await owner.PostAsync("/api/v1/onboarding/sample", null))).GetProperty("caseId").GetGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/cases/{id}")).StatusCode);

        var techMobile = ArteApiFactory.NewMobile();
        await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" });
        var (tech, _) = await api.LoginAsync(techMobile);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsync("/api/v1/onboarding/sample", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.DeleteAsync("/api/v1/onboarding/sample")).StatusCode);
    }

    [Fact]
    public async Task Client_errors_are_accepted_without_sign_in()
    {
        var res = await api.Client().PostAsJsonAsync("/api/v1/client-errors", new { message = "TypeError: x is undefined", url = "https://x/cases?q=secret" });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }
}
