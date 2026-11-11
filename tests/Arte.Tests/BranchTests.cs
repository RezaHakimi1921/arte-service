using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arte.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arte.Tests;

/// <summary>Brands and branches: only the platform admin creates them; a new branch gets the trial; the branches dashboard.</summary>
[Collection(ApiCollection.Name)]
public sealed class BranchTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return res.StatusCode == HttpStatusCode.NoContent ? default : await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task<HttpClient> AdminAsync()
    {
        var mobile = ArteApiFactory.NewMobile();
        var (client, _) = await api.LoginAsync(mobile);
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ArteDbContext>().Users.Where(u => u.Mobile == mobile)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsPlatformAdmin, true));
        return client;
    }

    private static async Task<Guid> OpenCase(HttpClient c, Guid? assigneeId = null) =>
        (await Json(await c.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "علی", reportedProblems = new[] { "روشن نمی‌شود" }, assigneeId,
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        }))).GetProperty("id").GetGuid();

    [Fact]
    public async Task Only_the_admin_creates_branches_and_a_new_branch_gets_the_trial()
    {
        var ownerMobile = ArteApiFactory.NewMobile();
        var (owner, first) = await api.NewBusinessAsync(ownerMobile);

        // Owners cannot add a second business themselves, nor use the admin endpoint.
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/v1/tenants", new { ownerName = "مالک تست" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/v1/admin/branches", new { name = "شعبه ۲", ownerMobile })).StatusCode);

        var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/admin/branches", new { name = "x", ownerMobile = "123" })).StatusCode);

        await Json(await owner.PostAsJsonAsync("/api/v1/catalog", new { kind = "part", title = "روغن ویژه", defaultPriceRials = 5_000_000 }));
        await Json(await owner.PutAsJsonAsync("/api/v1/settings/business", new { requireFinalReview = false, trackShowAmounts = false }));
        await OpenCase(owner);

        var created = await Json(await admin.PostAsJsonAsync("/api/v1/admin/branches", new
        {
            name = "تعمیرگاه تست - شعبه ۲", newBrandName = "برند تست", ownerMobile, businessType = "motorcycle_repair",
            copyFromId = first, copySettings = true, copyCatalog = true,
        }));
        var second = created.GetProperty("id").GetGuid();
        var brandId = created.GetProperty("brandId").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/v1/admin/businesses/{first}/brand", new { brandId })).StatusCode);

        var brands = await Json(await admin.GetAsync("/api/v1/admin/brands"));
        var brand = brands.EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == brandId);
        Assert.Equal(2, brand.GetProperty("branches").GetArrayLength());

        // The new branch: case-based trial licence, copied settings and price list, no cases.
        var detail = await Json(await admin.GetAsync($"/api/v1/admin/businesses/{second}"));
        var status = detail.GetProperty("status");
        Assert.Equal("trial", status.GetProperty("kind").GetString());
        Assert.True(status.GetProperty("onCaseTrial").GetBoolean());
        Assert.Equal(Arte.Core.Licensing.LicensePolicy.TrialCaseLimit, status.GetProperty("freeCasesLimit").GetInt32());
        Assert.Equal(Arte.Core.Licensing.LicensePolicy.TrialCaseLimit, status.GetProperty("freeCasesLeft").GetInt32());
        Assert.Equal(0, status.GetProperty("freeCasesUsed").GetInt32());
        Assert.Equal("برند تست", detail.GetProperty("brand").GetString());

        await ArteApiFactory.SelectAsync(owner, second);
        var settings = await Json(await owner.GetAsync("/api/v1/settings/business"));
        Assert.False(settings.GetProperty("requireFinalReview").GetBoolean());
        Assert.False(settings.GetProperty("trackShowAmounts").GetBoolean());
        Assert.Single((await Json(await owner.GetAsync("/api/v1/catalog?q=روغن ویژه"))).EnumerateArray());
        Assert.Empty((await Json(await owner.GetAsync("/api/v1/cases?all=true"))).EnumerateArray());
        Assert.Equal(2, (await Json(await owner.GetAsync("/api/v1/me"))).GetProperty("branchCount").GetInt32());
    }

    [Fact]
    public async Task The_admin_enters_any_branch_with_hidden_support_access_that_ends_with_the_admin_flag()
    {
        var (owner, tenantId) = await api.NewBusinessAsync();
        var caseId = await OpenCase(owner);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsync($"/api/v1/admin/businesses/{tenantId}/support", null)).StatusCode);

        var adminMobile = ArteApiFactory.NewMobile();
        var (admin, _) = await api.LoginAsync(adminMobile);
        await using (var scope = api.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ArteDbContext>().Users.Where(u => u.Mobile == adminMobile)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsPlatformAdmin, true));

        await Json(await admin.PostAsync($"/api/v1/admin/businesses/{tenantId}/support", null));
        var session = await Json(await admin.PostAsJsonAsync("/api/v1/auth/select-tenant", new { tenantId }));
        admin.DefaultRequestHeaders.Authorization = new("Bearer", session.GetProperty("accessToken").GetString());
        // Full access, flagged as support, and not listed as one of the admin's own branches.
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/v1/cases/{caseId}")).StatusCode);
        var me = await Json(await admin.GetAsync("/api/v1/me"));
        Assert.True(me.GetProperty("business").GetProperty("support").GetBoolean());
        Assert.Empty(session.GetProperty("memberships").EnumerateArray());

        // Hidden from the branch: staff list, assignees.
        var staff = await Json(await owner.GetAsync("/api/v1/staff"));
        Assert.DoesNotContain(staff.EnumerateArray(), m => m.GetProperty("mobile").GetString() == adminMobile);
        var assignable = await Json(await owner.GetAsync("/api/v1/staff/assignable"));
        Assert.Single(assignable.EnumerateArray());

        // No longer admin: the support session stops working at once.
        await using (var scope = api.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ArteDbContext>().Users.Where(u => u.Mobile == adminMobile)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsPlatformAdmin, false));
        Assert.Equal(HttpStatusCode.Unauthorized, (await admin.GetAsync($"/api/v1/cases/{caseId}")).StatusCode);
    }

    [Fact]
    public async Task An_admin_who_only_visited_a_branch_can_be_added_to_its_staff_as_a_real_member()
    {
        var (owner, tenantId) = await api.NewBusinessAsync();
        var adminMobile = ArteApiFactory.NewMobile();
        var (admin, _) = await api.LoginAsync(adminMobile);
        await using (var scope = api.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<ArteDbContext>().Users.Where(u => u.Mobile == adminMobile)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsPlatformAdmin, true));
        await Json(await admin.PostAsync($"/api/v1/admin/businesses/{tenantId}/support", null));

        var added = await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = adminMobile, displayName = "مدیر شعبه", role = "supervisor" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        var staff = await Json(await owner.GetAsync("/api/v1/staff"));
        var row = Assert.Single(staff.EnumerateArray(), m => m.GetProperty("mobile").GetString() == adminMobile);
        Assert.Equal("مدیر شعبه", row.GetProperty("displayName").GetString());
        // A second add is a normal duplicate now.
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = adminMobile, role = "supervisor" })).StatusCode);
    }

    [Fact]
    public async Task Sign_up_saves_the_owners_mobile_as_the_business_phone()
    {
        var mobile = ArteApiFactory.NewMobile();
        var (owner, _) = await api.NewBusinessAsync(mobile);
        Assert.Equal(mobile, (await Json(await owner.GetAsync("/api/v1/settings/business"))).GetProperty("phone").GetString());
    }

    [Fact]
    public async Task Switching_branch_twice_at_the_same_moment_does_not_fail()
    {
        var ownerMobile = ArteApiFactory.NewMobile();
        var (owner, first) = await api.NewBusinessAsync(ownerMobile);
        var admin = await AdminAsync();
        var second = (await Json(await admin.PostAsJsonAsync("/api/v1/admin/branches", new { name = "شعبه‌ی هم‌زمان", ownerMobile }))).GetProperty("id").GetGuid();

        // Two tabs (or a double start) revoke the same session family at once: both get a session, neither a 500.
        var results = await Task.WhenAll(
            owner.PostAsJsonAsync("/api/v1/auth/select-tenant", new { tenantId = second }),
            owner.PostAsJsonAsync("/api/v1/auth/select-tenant", new { tenantId = first }));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    }

    [Fact]
    public async Task The_dashboard_lists_my_branches_with_each_ones_numbers_and_my_own_view()
    {
        var ownerMobile = ArteApiFactory.NewMobile();
        var (owner, first) = await api.NewBusinessAsync(ownerMobile);
        var techMobile = ArteApiFactory.NewMobile();
        var techId = (await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" }))).GetProperty("id").GetGuid();
        await OpenCase(owner, techId);
        await OpenCase(owner);

        var admin = await AdminAsync();
        var second = (await Json(await admin.PostAsJsonAsync("/api/v1/admin/branches", new
        {
            name = "شعبه‌ی دوم", ownerMobile, copyFromId = first, copyStaff = true,
        }))).GetProperty("id").GetGuid();

        var rows = (await Json(await owner.GetAsync("/api/v1/branches"))).EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        var a = rows.Single(r => r.GetProperty("id").GetGuid() == first);
        Assert.Equal(2, a.GetProperty("open").GetInt32());
        Assert.Equal(0, rows.Single(r => r.GetProperty("id").GetGuid() == second).GetProperty("open").GetInt32());
        Assert.Equal("owner", a.GetProperty("role").GetString());
        Assert.NotEqual(JsonValueKind.Null, a.GetProperty("receivedTodayRials").ValueKind);

        // The technician is in both branches now (copied staff) and sees only their own work, and no money.
        var (tech, techSession) = await api.LoginAsync(techMobile);
        Assert.Equal(2, techSession.GetProperty("memberships").GetArrayLength());
        var theirs = (await Json(await tech.GetAsync("/api/v1/branches"))).EnumerateArray().ToList();
        Assert.Equal(2, theirs.Count);
        var t = theirs.Single(r => r.GetProperty("id").GetGuid() == first);
        Assert.Equal(1, t.GetProperty("open").GetInt32());
        Assert.Equal(JsonValueKind.Null, t.GetProperty("receivedTodayRials").ValueKind);

        // Someone else sees none of these.
        var (stranger, _) = await api.NewBusinessAsync();
        var others = (await Json(await stranger.GetAsync("/api/v1/branches"))).EnumerateArray().ToList();
        Assert.Single(others);
        Assert.DoesNotContain(others, r => r.GetProperty("id").GetGuid() == first);
    }

    [Fact]
    public async Task The_platform_admin_chooses_which_vehicle_kinds_a_business_takes_in()
    {
        var (owner, tenantId) = await api.NewBusinessAsync();
        var admin = await AdminAsync();

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/v1/admin/businesses/{tenantId}", new { vehicleKinds = new[] { "motorcycle" } })).StatusCode);
        var detail = await Json(await admin.GetAsync($"/api/v1/admin/businesses/{tenantId}"));
        Assert.Equal(["motorcycle"], detail.GetProperty("vehicleKinds").EnumerateArray().Select(x => x.GetString()).ToArray());
        var me = await Json(await owner.GetAsync("/api/v1/me"));
        Assert.Equal(["motorcycle"], me.GetProperty("business").GetProperty("vehicleKinds").EnumerateArray().Select(x => x.GetString()).ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/v1/admin/businesses/{tenantId}", new { vehicleKinds = Array.Empty<string>() })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PatchAsJsonAsync($"/api/v1/admin/businesses/{tenantId}", new { vehicleKinds = new[] { "boat" } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PatchAsJsonAsync($"/api/v1/admin/businesses/{tenantId}", new { vehicleKinds = new[] { "car" } })).StatusCode);
    }

    [Fact]
    public async Task A_short_neshan_link_is_accepted()
    {
        var (owner, _) = await api.NewBusinessAsync();
        Assert.True((await owner.PutAsJsonAsync("/api/v1/settings/business", new { mapNeshanUrl = "https://nshn.ir/7bvo2IyxRuFA" })).IsSuccessStatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/v1/settings/business", new { mapNeshanUrl = "https://evil.example/7bvo" })).StatusCode);
    }
}
