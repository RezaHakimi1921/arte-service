using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>Credit (نسیه) in the case list, paying it later, and a photo of the bank receipt with a payment.</summary>
[Collection(ApiCollection.Name)]
public sealed class PaymentReceiptTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static MultipartFormDataContent Receipt()
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(TinyPng);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "r.png");
        form.Add(new StringContent("receipt"), "purpose");
        return form;
    }

    /// <summary>A delivered case owing 1,000,000 rials (labor, delivered as credit).</summary>
    private static async Task<Guid> CreditCase(HttpClient owner)
    {
        var id = (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "رضا", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        }))).GetProperty("id").GetGuid();
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "labor", title = "تعمیر", unitPriceRials = 1_000_000 }));
        var c = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        while (c.GetProperty("stage").GetProperty("key").GetString() != "delivered")
        {
            var t = c.GetProperty("transitions").EnumerateArray().First(x => x.GetProperty("isPrimary").GetBoolean()).GetProperty("id");
            c = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{t}", new { allowCredit = true }));
        }
        return id;
    }

    [Fact]
    public async Task Credit_shows_in_the_list_and_is_paid_later_with_a_receipt()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var id = await CreditCase(owner);
        var row = (await Json(await owner.GetAsync("/api/v1/cases?all=true"))).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);
        Assert.True(row.GetProperty("isCredit").GetBoolean());
        Assert.Equal(1_000_000, row.GetProperty("balanceRials").GetInt64());

        // The receipt is uploaded first; it is not a gallery photo.
        var receiptId = (await Json(await owner.PostAsync($"/api/v1/cases/{id}/attachments", Receipt()))).GetProperty("id").GetGuid();
        var detail = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        Assert.Empty(detail.GetProperty("photos").EnumerateArray());

        var billing = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 1_000_000, method = "transfer", receiptId }));
        Assert.Equal(0, billing.GetProperty("money").GetProperty("balanceRials").GetInt64());
        Assert.Equal(receiptId, billing.GetProperty("payments")[0].GetProperty("receiptId").GetGuid());
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/v1/attachments/{receiptId}")).StatusCode);

        row = (await Json(await owner.GetAsync("/api/v1/cases?all=true"))).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);
        Assert.False(row.GetProperty("isCredit").GetBoolean());
    }

    [Fact]
    public async Task The_business_can_require_a_receipt_for_card_to_card()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var id = await CreditCase(owner);
        await Json(await owner.PutAsJsonAsync("/api/v1/settings/business", new { requireTransferReceipt = true }));

        var res = await owner.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 500_000, method = "transfer" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("receiptId", await res.Content.ReadAsStringAsync());
        // Cash needs no receipt.
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 500_000, method = "cash" }));

        // A receipt of another case is not accepted.
        var other = await CreditCase(owner);
        var foreign = (await Json(await owner.PostAsync($"/api/v1/cases/{other}/attachments", Receipt()))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest,
            (await owner.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 500_000, method = "transfer", receiptId = foreign })).StatusCode);
    }

    [Fact]
    public async Task Only_members_who_record_payments_upload_receipts()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techMobile = ArteApiFactory.NewMobile();
        var techId = (await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" }))).GetProperty("id").GetGuid();
        var id = (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "رضا", reportedProblems = new[] { "روشن نمی‌شود" }, assigneeId = techId,
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        }))).GetProperty("id").GetGuid();
        var (tech, _) = await api.LoginAsync(techMobile);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsync($"/api/v1/cases/{id}/attachments", Receipt())).StatusCode);

        // Given «ثبت پرداخت», the technician can attach the receipt to their own case.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/v1/staff/{techId}", new { permissions = new[] { "cases.work", "payments.record" } })).StatusCode);
        // Permissions are read on every request: no new sign-in needed.
        Assert.Equal(HttpStatusCode.Created, (await tech.PostAsync($"/api/v1/cases/{id}/attachments", Receipt())).StatusCode);
    }
}
