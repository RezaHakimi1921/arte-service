using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

[Collection(ApiCollection.Name)]
public sealed class BillingTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> OpenCase(HttpClient client, object? extra = null)
    {
        var created = await Json(await client.PostAsJsonAsync("/api/v1/cases", extra ?? new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "مشتری", request = "سرویس",
            newAsset = new { title = "پژو ۲۰۶", identifier = "12ب345-11", kind = "car" },
        }));
        return await Json(await client.GetAsync($"/api/v1/cases/{created.GetProperty("id")}"));
    }

    private static long Money(JsonElement billing, string field) => billing.GetProperty("money").GetProperty(field).GetInt64();

    [Fact]
    public async Task Totals_count_shop_lines_only_and_owner_sees_cost_and_profit()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var c = await OpenCase(owner);
        var id = c.GetProperty("id");

        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items",
            new { kind = "part", title = "روغن موتور", quantity = 4, unitPriceRials = 2_000_000, unitCostRials = 1_500_000 }));
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items",
            new { kind = "part", title = "فیلتر (مشتری آورد)", unitPriceRials = 900_000, supplier = "customer" }));
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items",
            new { kind = "part", title = "لنت (هنوز نرسیده)", unitPriceRials = 3_000_000, status = "needed" }));
        var billing = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items",
            new { kind = "labor", title = "اجرت تعویض روغن", unitPriceRials = 1_000_000, discountRials = 200_000 }));

        Assert.Equal(8_000_000 + 800_000, Money(billing, "totalRials"));
        Assert.Equal(8_000_000, Money(billing, "partsRials"));
        Assert.Equal(800_000, Money(billing, "laborRials"));
        Assert.Equal(6_000_000, Money(billing, "costRials"));
        Assert.Equal(2_800_000, Money(billing, "profitRials"));
        // Labor defaults to the person who recorded it.
        var labor = billing.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("kind").GetString() == "labor");
        Assert.NotEqual(JsonValueKind.Null, labor.GetProperty("performedBy").ValueKind);
    }

    [Fact]
    public async Task Technician_adds_labor_on_own_job_but_never_sees_or_sets_purchase_price()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techMobile = ArteApiFactory.NewMobile();
        var techId = (await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" }))).GetProperty("id").GetGuid();
        var c = await OpenCase(owner, new
        {
            mobile = ArteApiFactory.NewMobile(), request = "x", assigneeId = techId,
            newAsset = new { title = "پراید", identifier = "45ج678-22", kind = "car" },
        });
        var id = c.GetProperty("id");
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "part", title = "شمع", unitPriceRials = 500_000, unitCostRials = 300_000 }));
        var (tech, _) = await api.LoginAsync(techMobile);

        var view = await Json(await tech.GetAsync($"/api/v1/cases/{id}"));
        var billing = view.GetProperty("billing");
        Assert.False(billing.GetProperty("canSeeCost").GetBoolean());
        Assert.Equal(JsonValueKind.Null, billing.GetProperty("money").GetProperty("profitRials").ValueKind);
        Assert.All(billing.GetProperty("items").EnumerateArray(), i => Assert.Equal(JsonValueKind.Null, i.GetProperty("unitCostRials").ValueKind));

        Assert.Equal(HttpStatusCode.BadRequest, (await tech.PostAsJsonAsync($"/api/v1/cases/{id}/items",
            new { kind = "part", title = "x", unitPriceRials = 1, unitCostRials = 1 })).StatusCode);
        await Json(await tech.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "labor", title = "تعویض شمع", unitPriceRials = 300_000 }));
        // Payments are not theirs.
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 1, method = "cash" })).StatusCode);
    }

    [Fact]
    public async Task Deposit_payment_void_and_delivery_on_credit_with_warranty()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var c = await OpenCase(owner);
        var id = c.GetProperty("id");
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "labor", title = "تعمیر گیربکس", unitPriceRials = 10_000_000, warrantyDays = 90 }));
        var b = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 3_000_000, method = "card", note = "بیعانه" }));
        Assert.Equal(7_000_000, Money(b, "balanceRials"));

        var wrong = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 99, method = "cash" }));
        var wrongId = wrong.GetProperty("payments").EnumerateArray().Last().GetProperty("id").GetGuid();
        b = await Json(await owner.DeleteAsync($"/api/v1/cases/{id}/payments/{wrongId}"));
        Assert.Equal(7_000_000, Money(b, "balanceRials"));

        // Walk to "ready".
        var detail = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        while (detail.GetProperty("stage").GetProperty("key").GetString() != "ready")
        {
            var primary = detail.GetProperty("transitions").EnumerateArray().First(t => t.GetProperty("isPrimary").GetBoolean()).GetProperty("id");
            detail = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{primary}", new { }));
        }
        var deliver = detail.GetProperty("transitions").EnumerateArray().First(t => t.GetProperty("isPrimary").GetBoolean()).GetProperty("id");

        var refused = await owner.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{deliver}", new { });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("balance_due", await refused.Content.ReadAsStringAsync());

        detail = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{deliver}",
            new { allowCredit = true, creditDueAt = DateTimeOffset.UtcNow.AddDays(7) }));
        Assert.Equal("delivered", detail.GetProperty("stage").GetProperty("key").GetString());
        Assert.NotEqual(JsonValueKind.Null, detail.GetProperty("warrantyUntil").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, detail.GetProperty("creditDueAt").ValueKind);

        var receivables = await Json(await owner.GetAsync("/api/v1/receivables"));
        Assert.Equal(7_000_000, receivables.GetProperty("totalRials").GetInt64());
    }

    [Fact]
    public async Task Fully_paid_case_delivers_without_asking()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var c = await OpenCase(owner);
        var id = c.GetProperty("id");
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "service", title = "سرویس کامل", unitPriceRials = 5_000_000 }));
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 5_000_000, method = "cash" }));
        var detail = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        while (detail.GetProperty("stage").GetProperty("key").GetString() != "delivered")
        {
            var primary = detail.GetProperty("transitions").EnumerateArray().First(t => t.GetProperty("isPrimary").GetBoolean()).GetProperty("id");
            detail = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{primary}", new { }));
        }
        Assert.Equal(0, detail.GetProperty("billing").GetProperty("money").GetProperty("balanceRials").GetInt64());
    }

    [Fact]
    public async Task Item_soft_delete_and_restore_and_other_business_gets_404()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (other, _) = await api.NewBusinessAsync();
        var c = await OpenCase(owner);
        var id = c.GetProperty("id");
        var b = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "part", title = "باتری", unitPriceRials = 4_000_000 }));
        var itemId = b.GetProperty("items")[0].GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "part", title = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/cases/{id}/items/{itemId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 1, method = "cash" })).StatusCode);

        b = await Json(await owner.DeleteAsync($"/api/v1/cases/{id}/items/{itemId}"));
        Assert.Equal(0, Money(b, "totalRials"));
        b = await Json(await owner.PostAsync($"/api/v1/cases/{id}/items/{itemId}/restore", null));
        Assert.Equal(4_000_000, Money(b, "totalRials"));
    }

    [Fact]
    public async Task Catalog_prefills_lines_and_can_be_deactivated()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var cat = await Json(await owner.PostAsJsonAsync("/api/v1/catalog",
            new { kind = "service", title = "تعویض روغن کامل", defaultPriceRials = 1_200_000, defaultCostRials = 0, defaultWarrantyDays = 30 }));
        var c = await OpenCase(owner);
        var b = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{c.GetProperty("id")}/items", new { catalogItemId = cat.GetProperty("id").GetGuid() }));
        var line = b.GetProperty("items")[0];
        Assert.Equal("تعویض روغن کامل", line.GetProperty("title").GetString());
        Assert.Equal(1_200_000, line.GetProperty("unitPriceRials").GetInt64());
        Assert.Equal(30, line.GetProperty("warrantyDays").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/v1/catalog/{cat.GetProperty("id")}", new { isActive = false })).StatusCode);
        Assert.Equal(0, (await Json(await owner.GetAsync("/api/v1/catalog"))).GetArrayLength());
    }
}
