using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arte.Api.Licensing;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Licensing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arte.Tests;

[Collection(ApiCollection.Name)]
public sealed class LicensingTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    /// <summary>A signed-in user made platform admin the only way it can happen: directly in the database (server CLI).</summary>
    private async Task<HttpClient> AdminAsync()
    {
        var mobile = ArteApiFactory.NewMobile();
        var (client, _) = await api.LoginAsync(mobile);
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
        await db.Users.Where(u => u.Mobile == mobile).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsPlatformAdmin, true));
        return client;
    }

    private static async Task<JsonElement> Me(HttpClient c) => await c.GetFromJsonAsync<JsonElement>("/api/v1/me");

    private static async Task RevokeAll(HttpClient admin, Guid tenantId)
    {
        var detail = await Json(await admin.GetAsync($"/api/v1/admin/businesses/{tenantId}"));
        foreach (var l in detail.GetProperty("licenses").EnumerateArray().Where(l => !l.GetProperty("revoked").GetBoolean()))
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/admin/licenses/{l.GetProperty("id").GetGuid()}")).StatusCode);
    }

    [Fact]
    public async Task New_business_starts_with_a_14_day_trial_and_sees_the_price_list()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var license = (await Me(owner)).GetProperty("business").GetProperty("license");
        Assert.Equal("active", license.GetProperty("state").GetString());
        Assert.Equal("trial", license.GetProperty("kind").GetString());
        Assert.Equal(14, license.GetProperty("daysLeft").GetInt32());

        var mine = await Json(await owner.GetAsync("/api/v1/license"));
        var plans = mine.GetProperty("plans").EnumerateArray().ToList();
        Assert.Contains(plans, p => p.GetProperty("months").GetInt32() == 12 && p.GetProperty("priceRials").GetInt64() == 100_000_000);
        Assert.Single(mine.GetProperty("history").EnumerateArray());
    }

    [Fact]
    public async Task Expired_branch_keeps_working_on_existing_cases_but_cannot_take_new_ones()
    {
        var admin = await AdminAsync();
        var (owner, tenantId) = await api.NewBusinessAsync();
        var caseId = (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new { mobile = ArteApiFactory.NewMobile(), request = "صدا" })))
            .GetProperty("id").GetGuid();

        await RevokeAll(admin, tenantId);
        Assert.Equal("expired", (await Me(owner)).GetProperty("business").GetProperty("license").GetProperty("state").GetString());

        var blocked = await owner.PostAsJsonAsync("/api/v1/cases", new { mobile = ArteApiFactory.NewMobile(), request = "x" });
        Assert.Equal(HttpStatusCode.PaymentRequired, blocked.StatusCode);
        Assert.Contains("license_expired", await blocked.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.PaymentRequired, (await owner.PostAsJsonAsync("/API/V1/Cases/", new { mobile = ArteApiFactory.NewMobile(), request = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.PaymentRequired, (await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = ArteApiFactory.NewMobile(), role = "technician" })).StatusCode);

        // Existing work goes on: reading, items, payments.
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/cases")).StatusCode);
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{caseId}/items", new { kind = "labor", title = "اجرت", unitPriceRials = 1_000_000 }));
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{caseId}/payments", new { amountRials = 1_000_000, method = "cash" }));

        // A paid plan brings intake back.
        var plan = (await Json(await admin.GetAsync("/api/v1/admin/plans"))).EnumerateArray().First(p => p.GetProperty("months").GetInt32() == 3);
        await Json(await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{tenantId}/licenses", new { kind = "paid", planId = plan.GetProperty("id").GetGuid() }));
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/v1/cases", new { mobile = ArteApiFactory.NewMobile(), request = "y" })).StatusCode);
    }

    [Fact]
    public async Task A_new_licence_starts_where_the_current_one_ends()
    {
        var admin = await AdminAsync();
        var (owner, tenantId) = await api.NewBusinessAsync();
        var trialEnd = (await Me(owner)).GetProperty("business").GetProperty("license").GetProperty("endsAt").GetDateTimeOffset();

        var plan = (await Json(await admin.GetAsync("/api/v1/admin/plans"))).EnumerateArray().First(p => p.GetProperty("months").GetInt32() == 1);
        var granted = await Json(await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{tenantId}/licenses",
            new { kind = "paid", planId = plan.GetProperty("id").GetGuid(), note = "کارت به کارت" }));
        Assert.Equal(trialEnd, granted.GetProperty("startsAt").GetDateTimeOffset());
        Assert.Equal(trialEnd.AddMonths(1), granted.GetProperty("endsAt").GetDateTimeOffset());

        var gift = await Json(await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{tenantId}/licenses", new { kind = "gift", days = 7 }));
        Assert.Equal(trialEnd.AddMonths(1).AddDays(7), gift.GetProperty("endsAt").GetDateTimeOffset());

        var bad = await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{tenantId}/licenses", new { kind = "paid" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Inactive_branch_can_do_nothing_until_switched_back_on()
    {
        var admin = await AdminAsync();
        var (owner, tenantId) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/v1/admin/businesses/{tenantId}", new { isActive = false, reason = "پرداخت نشده" })).StatusCode);

        var res = await owner.GetAsync("/api/v1/cases");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
        Assert.Contains("branch_inactive", await res.Content.ReadAsStringAsync());
        Assert.False((await Me(owner)).GetProperty("business").GetProperty("isActive").GetBoolean());

        await admin.PatchAsJsonAsync($"/api/v1/admin/businesses/{tenantId}", new { isActive = true });
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/cases")).StatusCode);
    }

    [Fact]
    public async Task Admin_panel_is_only_for_platform_admins()
    {
        var (owner, tenantId) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/admin/businesses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync($"/api/v1/admin/businesses/{tenantId}/licenses", new { kind = "gift", days = 30 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PatchAsJsonAsync($"/api/v1/admin/businesses/{tenantId}", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client().GetAsync("/api/v1/admin/businesses")).StatusCode);
        Assert.False((await Me(owner)).GetProperty("isPlatformAdmin").GetBoolean());

        var admin = await AdminAsync();
        var list = await Json(await admin.GetAsync("/api/v1/admin/businesses"));
        var row = list.EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == tenantId);
        Assert.Equal("مالک تست", row.GetProperty("owner").GetProperty("displayName").GetString());
        Assert.Equal("trial", row.GetProperty("license").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Admin_edits_plan_prices_with_validation()
    {
        var admin = await AdminAsync();
        var created = await Json(await admin.PostAsJsonAsync("/api/v1/admin/plans", new { name = "دو ماهه آزمایشی", months = 2, priceRials = 18_000_000, isActive = false }));
        var id = created.GetProperty("id").GetGuid();
        var updated = await Json(await admin.PatchAsJsonAsync($"/api/v1/admin/plans/{id}", new { priceRials = 17_000_000 }));
        Assert.Equal(17_000_000, updated.GetProperty("priceRials").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/v1/admin/plans/{id}", new { months = 0 })).StatusCode);

        // An inactive plan is not offered to businesses.
        var (owner, _) = await api.NewBusinessAsync();
        var offered = await Json(await owner.GetAsync("/api/v1/license"));
        Assert.DoesNotContain(offered.GetProperty("plans").EnumerateArray(), p => p.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task Backfill_gives_old_businesses_one_trial_and_never_undoes_a_revoke()
    {
        var (_, fresh) = await api.NewBusinessAsync();
        var (_, revoked) = await api.NewBusinessAsync();
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
        var now = scope.ServiceProvider.GetRequiredService<IClock>().UtcNow;

        // "fresh" looks like a business from before licences; "revoked" had its licence taken away.
        await db.Licenses.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).Where(l => l.TenantId == fresh).ExecuteDeleteAsync();
        await db.Licenses.Where(l => l.TenantId == revoked).ExecuteUpdateAsync(s => s.SetProperty(l => l.DeletedAt, now));

        await LicenseService.BackfillAsync(db, now, CancellationToken.None);
        await LicenseService.BackfillAsync(db, now, CancellationToken.None);

        Assert.Equal(1, await db.Licenses.CountAsync(l => l.TenantId == fresh && l.Kind == LicenseKinds.Trial));
        Assert.Equal(0, await db.Licenses.CountAsync(l => l.TenantId == revoked));
    }

    [Fact]
    public async Task Admin_who_owns_a_branch_can_still_manage_another_branch()
    {
        var adminMobile = ArteApiFactory.NewMobile();
        var (admin, _) = await api.NewBusinessAsync(adminMobile);   // signed in with their own branch selected
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
            await db.Users.Where(u => u.Mobile == adminMobile).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsPlatformAdmin, true));
        }
        var (_, other) = await api.NewBusinessAsync();

        await Json(await admin.PostAsJsonAsync($"/api/v1/admin/businesses/{other}/licenses", new { kind = "gift", days = 10 }));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/v1/admin/businesses/{other}", new { isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/v1/admin/businesses/{other}", new { isActive = true })).StatusCode);
    }
}
