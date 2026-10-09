using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>Customer tracking link, customer SMS, business type and vehicle kinds.</summary>
[Collection(ApiCollection.Name)]
public sealed class TrackingTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<(Guid Id, string Code, string Mobile)> OpenCase(HttpClient owner)
    {
        var mobile = ArteApiFactory.NewMobile();
        var id = (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile, customerName = "سیما کریمی", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        }))).GetProperty("id").GetGuid();
        var code = (await Json(await owner.GetAsync($"/api/v1/cases/{id}"))).GetProperty("trackingCode").GetString()!;
        return (id, code, mobile);
    }

    [Fact]
    public async Task Tracking_link_shows_the_case_without_signing_in_and_without_private_data()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (id, code, mobile) = await OpenCase(owner);
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "part", title = "شمع", unitCostRials = 1_000_000, unitPriceRials = 1_500_000 }));

        var res = await api.Client().GetAsync($"/api/v1/track/{code}");
        var body = await res.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var t = JsonDocument.Parse(body).RootElement;
        Assert.Equal("تعمیرگاه تست", t.GetProperty("shop").GetProperty("name").GetString());
        Assert.Equal("received", t.GetProperty("status").GetProperty("key").GetString());
        Assert.Equal("current", t.GetProperty("stages")[0].GetProperty("state").GetString());
        Assert.Equal("شمع", t.GetProperty("items")[0].GetProperty("title").GetString());
        Assert.Equal(1_500_000, t.GetProperty("money").GetProperty("totalRials").GetInt64());
        // Never the customer's mobile, never the purchase price.
        Assert.DoesNotContain(mobile, body);
        Assert.DoesNotContain("1000000", body);
    }

    [Fact]
    public async Task Business_settings_decide_what_the_customer_sees()
    {
        var (owner, _) = await api.NewBusinessAsync();
        await Json(await owner.PutAsJsonAsync("/api/v1/settings/business", new { trackShowStages = false, trackShowItems = false, trackShowAmounts = false }));
        var (_, code, _) = await OpenCase(owner);
        var t = await Json(await api.Client().GetAsync($"/api/v1/track/{code}"));
        Assert.Equal(JsonValueKind.Null, t.GetProperty("stages").ValueKind);
        Assert.Equal(JsonValueKind.Null, t.GetProperty("items").ValueKind);
        Assert.Equal(JsonValueKind.Null, t.GetProperty("money").ValueKind);
        Assert.Equal("received", t.GetProperty("status").GetProperty("key").GetString());   // always shown
    }

    [Fact]
    public async Task Unknown_malformed_deleted_and_sample_codes_are_not_found()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var client = api.Client();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/track/abcdefghjk")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/track/'%20or%201=1")).StatusCode);

        var (id, code, _) = await OpenCase(owner);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/cases/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/track/{code}")).StatusCode);

        var sampleId = (await Json(await owner.PostAsync("/api/v1/onboarding/sample", null))).GetProperty("caseId").GetGuid();
        var sampleCode = (await Json(await owner.GetAsync($"/api/v1/cases/{sampleId}"))).GetProperty("trackingCode").GetString();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/track/{sampleCode}")).StatusCode);
    }

    [Fact]
    public async Task Customer_sms_only_when_the_business_switched_it_on()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (_, _, quiet) = await OpenCase(owner);
        Assert.False(api.Sms.Keys.ContainsKey(quiet));

        await Json(await owner.PutAsJsonAsync("/api/v1/settings/business", new { customerSmsEnabled = true, smsOnReady = true }));
        var (id, code, mobile) = await OpenCase(owner);
        Assert.Equal(["case.opened"], api.Sms.Keys[mobile]);
        var tokens = api.Sms.Sent[mobile];
        Assert.Equal(code, tokens["code"]);
        Assert.Equal("سیما کریمی", tokens["name"]);
        Assert.Equal("موتور هوندا CG 125", tokens["vehicle"]);
        Assert.Equal("تعمیرگاه تست", tokens["shop"]);

        // Walk the case to «ready»: the ready SMS goes out once, and the timeline records both.
        var c = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        while (c.GetProperty("stage").GetProperty("key").GetString() != "ready")
        {
            var primary = c.GetProperty("transitions").EnumerateArray().First(t => t.GetProperty("isPrimary").GetBoolean()).GetProperty("id");
            c = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{primary}", new { }));
        }
        Assert.Equal(["case.opened", "case.ready"], api.Sms.Keys[mobile]);
        Assert.Equal(2, c.GetProperty("timeline").EnumerateArray().Count(e => e.GetProperty("type").GetString() == "customer.sms"));
    }

    [Fact]
    public async Task Business_type_at_sign_up_picks_the_vehicle_kinds()
    {
        var (client, _) = await api.LoginAsync(ArteApiFactory.NewMobile());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/tenants", new { ownerName = "رضا", businessType = "bakery" })).StatusCode);
        var tenant = await Json(await client.PostAsJsonAsync("/api/v1/tenants", new { ownerName = "رضا نوری", businessType = "car_repair" }));
        var session = await Json(await client.PostAsJsonAsync("/api/v1/auth/select-tenant", new { tenantId = tenant.GetProperty("id").GetGuid() }));
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        var me = await client.GetFromJsonAsync<JsonElement>("/api/v1/me");
        Assert.Equal("car_repair", me.GetProperty("business").GetProperty("businessType").GetString());
        Assert.DoesNotContain("motorcycle", me.GetProperty("business").GetProperty("vehicleKinds").EnumerateArray().Select(k => k.GetString()));

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/v1/settings/business", new { vehicleKinds = Array.Empty<string>() })).StatusCode);
        var updated = await Json(await client.PutAsJsonAsync("/api/v1/settings/business", new { vehicleKinds = new[] { "car", "motorcycle" } }));
        Assert.Equal(2, updated.GetProperty("vehicleKinds").GetArrayLength());
    }
}
