using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>Workshop plan 1C: card readers and cards, receipts from the customer, payment before work, contact info on the customer page.</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkshopFinanceTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<(Guid Id, string Code)> OpenCase(HttpClient owner)
    {
        var id = (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "مشتری", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        }))).GetProperty("id").GetGuid();
        var code = (await Json(await owner.GetAsync($"/api/v1/cases/{id}"))).GetProperty("trackingCode").GetString()!;
        return (id, code);
    }

    private static MultipartFormDataContent Receipt(byte[]? bytes = null, string? amount = null)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes ?? [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4, 5, 6, 7, 8, 9]);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        form.Add(file, "file", "r.jpg");
        if (amount is not null) form.Add(new StringContent(amount), "amountRials");
        return form;
    }

    private async Task<HttpClient> TechAsync(HttpClient owner)
    {
        var mobile = ArteApiFactory.NewMobile();
        (await owner.PostAsJsonAsync("/api/v1/staff", new { mobile, displayName = "شاگرد", role = "technician" })).EnsureSuccessStatusCode();
        return (await api.LoginAsync(mobile)).Item1;
    }

    [Fact]
    public async Task Terminals_and_cards_are_managed_in_settings_and_chosen_when_recording_a_payment()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var tech = await TechAsync(owner);
        var terminal = (await Json(await owner.PostAsJsonAsync("/api/v1/payment-terminals", new { name = "کارتخوان ملی", bank = "ملی" }))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/bank-cards", new { cardNumber = "123", holderName = "رضا" })).StatusCode);
        var card = (await Json(await owner.PostAsJsonAsync("/api/v1/bank-cards", new { cardNumber = "۶۰۳۷-۹۹۷۱-۱۲۳۴-۵۶۷۸", holderName = "رضا", showOnTracking = true }))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync("/api/v1/payment-terminals", new { name = "x" })).StatusCode);

        var (id, code) = await OpenCase(owner);
        // The reader belongs to method card, the card to method transfer.
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 1000, method = "cash", terminalId = terminal })).StatusCode);
        (await owner.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 1000, method = "card", terminalId = terminal })).EnsureSuccessStatusCode();
        (await owner.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 2000, method = "transfer", cardId = card })).EnsureSuccessStatusCode();

        var track = await Json(await api.Client().GetAsync($"/api/v1/track/{code}"));
        Assert.Equal("6037997112345678", track.GetProperty("pay").GetProperty("card").GetProperty("cardNumber").GetString());

        // Switched off: no longer offered or shown.
        (await owner.PatchAsJsonAsync($"/api/v1/bank-cards/{card}", new { isActive = false })).EnsureSuccessStatusCode();
        track = await Json(await api.Client().GetAsync($"/api/v1/track/{code}"));
        Assert.Equal(JsonValueKind.Null, track.GetProperty("pay").GetProperty("card").ValueKind);
    }

    [Fact]
    public async Task Customer_sends_a_receipt_and_the_shop_approves_it_as_a_payment()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (other, _) = await api.NewBusinessAsync();
        var tech = await TechAsync(owner);
        var (id, code) = await OpenCase(owner);
        var anon = api.Client();

        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsync($"/api/v1/track/{code}/receipts", Receipt([1, 2, 3, 4, 5, 6]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anon.PostAsync("/api/v1/track/zzzzzzzz/receipts", Receipt())).StatusCode);
        (await anon.PostAsync($"/api/v1/track/{code}/receipts", Receipt(amount: "۵۰۰۰۰۰"))).EnsureSuccessStatusCode();

        var track = await Json(await anon.GetAsync($"/api/v1/track/{code}"));
        Assert.Equal("pending", track.GetProperty("pay").GetProperty("receipts")[0].GetProperty("status").GetString());

        var list = await Json(await owner.GetAsync($"/api/v1/cases/{id}/customer-receipts"));
        var rid = list[0].GetProperty("id").GetGuid();
        Assert.Equal(500_000, list[0].GetProperty("claimedRials").GetInt64());
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsync($"/api/v1/customer-receipts/{rid}/approve", JsonContent.Create(new { }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/customer-receipts/{rid}/approve", new { })).StatusCode);

        (await owner.PostAsJsonAsync($"/api/v1/customer-receipts/{rid}/approve", new { })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/v1/customer-receipts/{rid}/approve", new { })).StatusCode);
        var billing = await Json(await owner.GetAsync($"/api/v1/cases/{id}/billing"));
        Assert.Equal(500_000, billing.GetProperty("money").GetProperty("paidRials").GetInt64());

        // A rejected receipt records nothing and tells the customer why.
        (await anon.PostAsync($"/api/v1/track/{code}/receipts", Receipt(amount: "1000"))).EnsureSuccessStatusCode();
        var second = (await Json(await owner.GetAsync($"/api/v1/cases/{id}/customer-receipts"))).EnumerateArray().First(r => r.GetProperty("status").GetString() == "pending").GetProperty("id").GetGuid();
        (await owner.PostAsJsonAsync($"/api/v1/customer-receipts/{second}/reject", new { note = "مبلغ نمی‌خواند" })).EnsureSuccessStatusCode();
        track = await Json(await anon.GetAsync($"/api/v1/track/{code}"));
        Assert.Contains(track.GetProperty("pay").GetProperty("receipts").EnumerateArray(), r => r.GetProperty("note").GetString() == "مبلغ نمی‌خواند");
        billing = await Json(await owner.GetAsync($"/api/v1/cases/{id}/billing"));
        Assert.Equal(500_000, billing.GetProperty("money").GetProperty("paidRials").GetInt64());
    }

    [Fact]
    public async Task Only_a_few_receipts_can_wait_at_the_same_time()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (_, code) = await OpenCase(owner);
        for (var i = 0; i < 3; i++) (await api.Client().PostAsync($"/api/v1/track/{code}/receipts", Receipt())).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.TooManyRequests, (await api.Client().PostAsync($"/api/v1/track/{code}/receipts", Receipt())).StatusCode);
    }

    [Fact]
    public async Task When_payment_comes_first_work_waits_for_a_recorded_payment()
    {
        var (owner, _) = await api.NewBusinessAsync();
        (await owner.PutAsJsonAsync("/api/v1/settings/business", new { requirePaymentBeforeWork = true })).EnsureSuccessStatusCode();
        var (id, _) = await OpenCase(owner);

        async Task<HttpResponseMessage> Start()
        {
            var detail = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
            var t = detail.GetProperty("transitions").EnumerateArray().First(x => x.GetProperty("toStage").GetProperty("name").GetString() == "در حال تعمیر").GetProperty("id").GetGuid();
            return await owner.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{t}", new { });
        }
        var blocked = await Start();
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("payment_required", await blocked.Content.ReadAsStringAsync());

        (await owner.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 1000, method = "cash" })).EnsureSuccessStatusCode();
        (await Start()).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Customer_page_shows_the_shop_contact_the_owner_chose()
    {
        var (owner, _) = await api.NewBusinessAsync();
        (await owner.PutAsJsonAsync("/api/v1/settings/business", new { phone = "02112345678" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/v1/settings/business", new { instagramId = "bad id!" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/v1/settings/business", new { mapGoogleUrl = "https://evil.example.com/maps" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/v1/settings/business", new { mapWazeUrl = "javascript:alert(1)" })).StatusCode);
        (await owner.PutAsJsonAsync("/api/v1/settings/business", new { instagramId = "@my_shop", mapNeshanUrl = "https://neshan.org/maps/x", mapBaladUrl = "https://balad.ir/p/x" })).EnsureSuccessStatusCode();
        var (_, code) = await OpenCase(owner);

        var shop = (await Json(await api.Client().GetAsync($"/api/v1/track/{code}"))).GetProperty("shop");
        Assert.Equal("my_shop", shop.GetProperty("instagram").GetString());
        Assert.Equal(new[] { "neshan", "balad" }, shop.GetProperty("maps").EnumerateArray().Select(m => m.GetProperty("key").GetString()!).ToArray());
        Assert.Equal("02112345678", shop.GetProperty("phone").GetString());

        (await owner.PutAsJsonAsync("/api/v1/settings/business", new { trackShowPhone = false })).EnsureSuccessStatusCode();
        shop = (await Json(await api.Client().GetAsync($"/api/v1/track/{code}"))).GetProperty("shop");
        Assert.Equal(JsonValueKind.Null, shop.GetProperty("phone").ValueKind);
    }
}
