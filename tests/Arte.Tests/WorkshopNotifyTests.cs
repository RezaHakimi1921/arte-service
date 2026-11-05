using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>Workshop plan 1D: SMS when waiting for the customer's part, SMS to the master when work is finished, push subscriptions.</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkshopNotifyTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> OpenCase(HttpClient owner)
    {
        return (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "مشتری", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        }))).GetProperty("id").GetGuid();
    }

    private static async Task Move(HttpClient client, Guid id, string stageName, object? body = null)
    {
        var detail = await Json(await client.GetAsync($"/api/v1/cases/{id}"));
        var t = detail.GetProperty("transitions").EnumerateArray().First(x => x.GetProperty("toStage").GetProperty("name").GetString() == stageName).GetProperty("id").GetGuid();
        await Json(await client.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{t}", body ?? new { }));
    }

    private static async Task<List<JsonElement>> Events(HttpClient client, Guid id) =>
        (await Json(await client.GetAsync($"/api/v1/cases/{id}"))).GetProperty("timeline").EnumerateArray().ToList();

    private static string? SmsKind(JsonElement e) =>
        e.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object && d.TryGetProperty("kind", out var k) ? k.GetString() : null;

    [Fact]
    public async Task Waiting_for_the_customers_part_sends_an_sms_only_when_the_setting_is_on()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var off = await OpenCase(owner);
        await Move(owner, off, "در حال عیب‌یابی");
        await Move(owner, off, "منتظر قطعه", new { waitReason = "customer_parts" });
        Assert.DoesNotContain(await Events(owner, off), e => SmsKind(e) == "case.waiting_part");

        (await owner.PutAsJsonAsync("/api/v1/settings/business", new { smsOnWaitingPart = true })).EnsureSuccessStatusCode();
        var on = await OpenCase(owner);
        await Move(owner, on, "در حال عیب‌یابی");
        await Move(owner, on, "منتظر قطعه", new { waitReason = "customer_parts" });
        Assert.Contains(await Events(owner, on), e => e.GetProperty("type").GetString() == "customer.sms" && SmsKind(e) == "case.waiting_part");

        // A part the shop buys is not the customer's business.
        var shop = await OpenCase(owner);
        await Move(owner, shop, "در حال عیب‌یابی");
        await Move(owner, shop, "منتظر قطعه", new { waitReason = "shop_parts" });
        Assert.DoesNotContain(await Events(owner, shop), e => SmsKind(e) == "case.waiting_part");
    }

    [Fact]
    public async Task Finishing_work_tells_the_master_by_sms_when_the_setting_is_on()
    {
        var (owner, _) = await api.NewBusinessAsync();
        (await owner.PutAsJsonAsync("/api/v1/settings/business", new { smsToMasterOnFinished = true })).EnsureSuccessStatusCode();
        var techMobile = ArteApiFactory.NewMobile();
        var added = await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, displayName = "شاگرد", role = "technician" }));
        var (tech, _) = await api.LoginAsync(techMobile);

        var id = (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "م", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" }, assigneeId = added.GetProperty("id").GetGuid(),
        }))).GetProperty("id").GetGuid();
        await Move(owner, id, "در حال تعمیر");
        await Move(tech, id, "بررسی نهایی استاد");

        Assert.Contains(await Events(owner, id), e => e.GetProperty("type").GetString() == "staff.sms");
    }

    [Fact]
    public async Task Push_subscription_is_validated_and_moves_with_the_device()
    {
        var (owner, _) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/push/subscribe", new { endpoint = "http://insecure.example", p256dh = "a", auth = "b" })).StatusCode);
        var ok = new { endpoint = "https://push.example.com/send/abc" + Guid.NewGuid().ToString("N"), p256dh = "BPkey", auth = "authkey" };
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync("/api/v1/push/subscribe", ok)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync("/api/v1/push/subscribe", ok)).StatusCode); // same device twice is fine
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync("/api/v1/push/unsubscribe", new { ok.endpoint })).StatusCode);

        var config = await Json(await owner.GetAsync("/api/v1/push/config"));
        Assert.False(config.GetProperty("enabled").GetBoolean()); // no VAPID keys in tests
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client().GetAsync("/api/v1/push/config")).StatusCode);
    }
}
