using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

[Collection(ApiCollection.Name)]
public sealed class DamageTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Guid Primary(JsonElement detail) =>
        detail.GetProperty("transitions").EnumerateArray().First(t => t.GetProperty("isPrimary").GetBoolean()).GetProperty("id").GetGuid();

    private static async Task<JsonElement> RunTransition(HttpClient client, Guid caseId, Guid transitionId) =>
        await Json(await client.PostAsJsonAsync($"/api/v1/cases/{caseId}/transitions/{transitionId}", new { }));

    private static async Task<(Guid Id, string Code)> OpenCase(HttpClient owner, string? bodyStatus = null)
    {
        var mobile = ArteApiFactory.NewMobile();
        var id = (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile, customerName = "خسارت", request = "تست",
            newAsset = new { title = "هوندا", kind = "motorcycle" },
            bodyStatus,
        }))).GetProperty("id").GetGuid();
        var detail = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        var code = detail.GetProperty("trackingCode").GetString()!;
        return (id, code);
    }

    private static async Task DeliverAsync(HttpClient owner, Guid caseId)
    {
        var detail = await Json(await owner.GetAsync($"/api/v1/cases/{caseId}"));
        foreach (var _ in new[] { "diagnosing", "repairing", "review", "ready", "delivered" })
        {
            detail = await RunTransition(owner, caseId, Primary(detail));
        }
        Assert.Equal("delivered", detail.GetProperty("stage").GetProperty("key").GetString());
    }

    [Fact]
    public async Task Owner_creates_customer_damage_technician_gets_403_other_tenant_404()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (otherOwner, _) = await api.NewBusinessAsync();
        var (id, _) = await OpenCase(owner);

        var damage = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/customer-damages",
            new { amountRials = 5_000_000, internalNote = "خط خطی باک" }));
        Assert.Equal(5_000_000, damage.GetProperty("amountRials").GetInt64());
        Assert.Equal(5_000_000, damage.GetProperty("remainingRials").GetInt64());

        var techMobile = ArteApiFactory.NewMobile();
        await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" }));
        var (tech, _) = await api.LoginAsync(techMobile);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync($"/api/v1/cases/{id}/customer-damages",
            new { amountRials = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherOwner.GetAsync($"/api/v1/cases/{id}/customer-damages")).StatusCode);
    }

    [Fact]
    public async Task Tracking_shows_damage_summary_without_internal_note_when_flag_on()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (id, code) = await OpenCase(owner);
        var secret = "این یادداشت داخلی نباید برود";
        var created = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/customer-damages",
            new { amountRials = 2_000_000, internalNote = secret, showReportToCustomer = true, showMediaToCustomer = false }));

        var body = await api.Client().GetStringAsync($"/api/v1/track/{code}");
        Assert.DoesNotContain(secret, body);
        var track = JsonDocument.Parse(body).RootElement;
        var damages = track.GetProperty("customerDamages").EnumerateArray().ToList();
        Assert.Single(damages);
        Assert.Equal(2_000_000, damages[0].GetProperty("amountRials").GetInt64());
        Assert.Equal(JsonValueKind.Null, damages[0].GetProperty("media").ValueKind);
    }

    [Fact]
    public async Task Compensation_maps_discount_and_total_for_customer()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (id, _) = await OpenCase(owner);
        var damage = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/customer-damages",
            new { amountRials = 1_000_000 }));
        var damageId = damage.GetProperty("id").GetGuid();

        var billing = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new
        {
            kind = "part", title = "تنه موتور", unitPriceRials = 800_000, status = "used", customerDamageId = damageId,
        }));
        var item = billing.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(800_000, item.GetProperty("discountRials").GetInt64());
        Assert.Equal(0, item.GetProperty("lineTotalRials").GetInt64());
        Assert.Equal(0, billing.GetProperty("money").GetProperty("totalRials").GetInt64());

        var damages = await Json(await owner.GetAsync($"/api/v1/cases/{id}/customer-damages"));
        Assert.Equal(200_000, damages[0].GetProperty("remainingRials").GetInt64());
    }

    [Fact]
    public async Task Excess_customer_purchase_adds_difference_to_total()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (id, _) = await OpenCase(owner);
        var damage = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/customer-damages",
            new { amountRials = 500_000 }));
        var damageId = damage.GetProperty("id").GetGuid();

        var billing = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new
        {
            kind = "part", title = "رینگ", unitPriceRials = 900_000, status = "used",
            customerDamageId = damageId, compensationExcessMode = "customer_purchase",
        }));
        Assert.Equal(400_000, billing.GetProperty("money").GetProperty("totalRials").GetInt64());
    }

    [Fact]
    public async Task Void_damage_requires_reason_and_lists_removed()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (id, _) = await OpenCase(owner);
        var damage = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/customer-damages",
            new { amountRials = 1 }));
        var damageId = damage.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync($"/api/v1/cases/{id}/customer-damages/{damageId}/void", new { reason = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await owner.PostAsJsonAsync($"/api/v1/cases/{id}/customer-damages/{damageId}/void", new { reason = "اشتباه" })).StatusCode);
        var list = await Json(await owner.GetAsync($"/api/v1/cases/{id}/customer-damages"));
        Assert.Empty(list.EnumerateArray());
    }

    [Fact]
    public async Task Mark_returned_on_delivered_does_not_reopen_or_change_body_status()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (id, _) = await OpenCase(owner, bodyStatus: "ok");
        await DeliverAsync(owner, id);

        var before = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        var stageBefore = before.GetProperty("stage").GetProperty("id").GetGuid();
        Assert.Equal("ok", before.GetProperty("bodyStatus").GetString());

        var marked = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/mark-returned",
            new { reason = "همان ایراد برگشت" }));
        Assert.NotEqual(JsonValueKind.Null, marked.GetProperty("returnMarkedAt").ValueKind);
        Assert.Equal(stageBefore, marked.GetProperty("priorStageId").GetGuid());
        Assert.Equal(stageBefore, marked.GetProperty("stageId").GetGuid());

        var after = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        Assert.Equal("delivered", after.GetProperty("stage").GetProperty("key").GetString());
        Assert.Equal("ok", after.GetProperty("bodyStatus").GetString());
        Assert.Contains(after.GetProperty("timeline").EnumerateArray(),
            e => e.GetProperty("type").GetString() == "case.return_marked");
    }

    [Fact]
    public async Task Damage_can_be_recorded_after_delivery_without_stage_change()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (id, _) = await OpenCase(owner);
        await DeliverAsync(owner, id);
        var stageId = (await Json(await owner.GetAsync($"/api/v1/cases/{id}"))).GetProperty("stage").GetProperty("id").GetGuid();

        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/customer-damages", new { amountRials = 3_000_000 }));
        var after = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        Assert.Equal(stageId, after.GetProperty("stage").GetProperty("id").GetGuid());
        Assert.Equal("delivered", after.GetProperty("stage").GetProperty("key").GetString());
    }
}