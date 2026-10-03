using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

[Collection(ApiCollection.Name)]
public sealed class PayPhotosReportsTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>Owner + technician; a delivered case: parts 3,000,000 (bought 2,500,000) + labor 1,000,000, assigned to the technician.</summary>
    private async Task<(HttpClient Owner, Guid TechId)> DeliveredCase(object pay)
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techId = (await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = ArteApiFactory.NewMobile(), role = "technician" })))
            .GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/v1/staff/{techId}", pay)).StatusCode);

        var created = await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), reportedProblems = new[] { "روشن نمی‌شود" }, assigneeId = techId,
            newAsset = new { title = "پژو ۲۰۶", kind = "car" },
        }));
        var id = created.GetProperty("id").GetGuid();
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "part", title = "قطعات", unitCostRials = 25_000_000, unitPriceRials = 30_000_000 }));
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "labor", title = "اجرت", unitPriceRials = 10_000_000 }));
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/payments", new { amountRials = 40_000_000, method = "cash" }));
        var c = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        while (c.GetProperty("stage").GetProperty("key").GetString() != "delivered")
        {
            var primary = c.GetProperty("transitions").EnumerateArray().First(t => t.GetProperty("isPrimary").GetBoolean()).GetProperty("id");
            c = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{primary}", new { }));
        }
        return (owner, techId);
    }

    private static async Task<JsonElement> StaffRow(HttpClient owner, Guid techId)
    {
        var from = DateTimeOffset.UtcNow.AddDays(-1).ToString("O");
        var to = DateTimeOffset.UtcNow.AddDays(1).ToString("O");
        var report = await Json(await owner.GetAsync($"/api/v1/reports/summary?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}"));
        Assert.Equal(1, report.GetProperty("delivered").GetInt32());
        Assert.Equal(40_000_000, report.GetProperty("salesRials").GetInt64());
        Assert.Equal(5_000_000, report.GetProperty("partsProfitRials").GetInt64());
        Assert.Equal(40_000_000, report.GetProperty("receivedRials").GetInt64());
        return report.GetProperty("staff").EnumerateArray().Single(s => s.GetProperty("membershipId").GetGuid() == techId);
    }

    [Theory]
    [InlineData("case_total", 8_000_000)]              // 20% × (3,000,000 + 1,000,000) = 800,000 toman
    [InlineData("labor", 2_000_000)]                   // 20% × 1,000,000
    [InlineData("labor_plus_parts_profit", 3_000_000)] // 20% × (1,000,000 + 500,000)
    public async Task Percent_commission_follows_the_chosen_base(string commissionBase, long expectedRials)
    {
        var (owner, techId) = await DeliveredCase(new { commissionType = "percent", commissionPercent = 20, commissionBase });
        var row = await StaffRow(owner, techId);
        Assert.Equal(1, row.GetProperty("cases").GetInt32());
        Assert.Equal(expectedRials, row.GetProperty("commissionRials").GetInt64());
    }

    [Fact]
    public async Task Fixed_commission_per_case_and_monthly_salary_and_name_edit()
    {
        var (owner, techId) = await DeliveredCase(new
        {
            commissionType = "fixed_per_case", commissionFixedRials = 1_500_000, fixedMonthlyRials = 150_000_000, displayName = "حسین",
        });
        var row = await StaffRow(owner, techId);
        Assert.Equal(1_500_000, row.GetProperty("commissionRials").GetInt64());
        Assert.Equal(150_000_000, row.GetProperty("fixedMonthlyRials").GetInt64());
        Assert.Equal("حسین", row.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Reports_and_pay_are_owner_only()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techMobile = ArteApiFactory.NewMobile();
        await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" });
        var (tech, _) = await api.LoginAsync(techMobile);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync($"/api/v1/reports/summary?from={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync("/api/v1/staff")).StatusCode);
    }

    [Fact]
    public async Task Purchase_price_cannot_exceed_sale_price()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var created = await Json(await owner.PostAsJsonAsync("/api/v1/cases", new { mobile = ArteApiFactory.NewMobile(), request = "x" }));
        var id = created.GetProperty("id");
        var bad = await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "part", title = "لنت", unitCostRials = 9_000_000, unitPriceRials = 8_000_000 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("unitCostRials", await bad.Content.ReadAsStringAsync());
        // Equal is fine; a customer-supplied part is not checked.
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "part", title = "لنت", unitCostRials = 8_000_000, unitPriceRials = 8_000_000 }));
        var b = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "part", title = "شمع", unitPriceRials = 1_000_000 }));
        var itemId = b.GetProperty("items").EnumerateArray().Last().GetProperty("id");
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync($"/api/v1/cases/{id}/items/{itemId}", new { unitCostRials = 2_000_000 })).StatusCode);
    }

    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static MultipartFormDataContent Form(byte[] bytes, string name = "photo.png")
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", name);
        form.Add(new StringContent("کار تمام شد"), "caption");
        return form;
    }

    [Fact]
    public async Task Photo_upload_download_isolation_and_type_check()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (other, _) = await api.NewBusinessAsync();
        var created = await Json(await owner.PostAsJsonAsync("/api/v1/cases", new { mobile = ArteApiFactory.NewMobile(), request = "x" }));
        var id = created.GetProperty("id");

        var uploaded = await Json(await owner.PostAsync($"/api/v1/cases/{id}/attachments", Form(TinyPng)));
        var photoId = uploaded.GetProperty("id").GetGuid();
        Assert.Equal("received", uploaded.GetProperty("stageKey").GetString());

        var download = await owner.GetAsync($"/api/v1/attachments/{photoId}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal("image/png", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal(TinyPng, await download.Content.ReadAsByteArrayAsync());

        var detail = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        Assert.Single(detail.GetProperty("photos").EnumerateArray());

        // Another business never gets it; a disguised file is refused; anonymous is refused.
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/attachments/{photoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/v1/cases/{id}/attachments", Form(TinyPng))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/v1/cases/{id}/attachments", Form("<?php echo 1; ?>"u8.ToArray(), "x.png"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client().GetAsync($"/api/v1/attachments/{photoId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/attachments/{photoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/attachments/{photoId}")).StatusCode);
    }
}
