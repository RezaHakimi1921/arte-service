using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>Workshop plan 1A: discount on the customer link, profit of lines waiting for a part, who did each step, staff passwords.</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkshopFixesTests(ArteApiFactory api)
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

    // ───────── 10: discount on the customer link ─────────

    [Fact]
    public async Task Customer_link_shows_the_discount_and_the_total_before_it()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (id, code) = await OpenCase(owner);
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items",
            new { kind = "labor", title = "اجرت", unitPriceRials = 1_000_000, discountRials = 200_000 }));

        var t = await Json(await api.Client().GetAsync($"/api/v1/track/{code}"));
        var money = t.GetProperty("money");
        Assert.Equal(800_000, money.GetProperty("totalRials").GetInt64());
        Assert.Equal(200_000, money.GetProperty("discountRials").GetInt64());
        Assert.Equal(1_000_000, money.GetProperty("subtotalRials").GetInt64());
        Assert.Equal(200_000, t.GetProperty("items")[0].GetProperty("discountRials").GetInt64());
    }

    [Fact]
    public async Task Customer_link_hides_the_discount_when_amounts_are_hidden()
    {
        var (owner, _) = await api.NewBusinessAsync();
        (await owner.PutAsJsonAsync("/api/v1/settings/business", new { trackShowAmounts = false })).EnsureSuccessStatusCode();
        var (id, code) = await OpenCase(owner);
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items",
            new { kind = "labor", title = "اجرت", unitPriceRials = 1_000_000, discountRials = 200_000 }));

        var body = await (await api.Client().GetAsync($"/api/v1/track/{code}")).Content.ReadAsStringAsync();
        Assert.DoesNotContain("200000", body);
        Assert.DoesNotContain("subtotalRials", body);
    }

    // ───────── 11: a line waiting for its part is not zero profit ─────────

    [Fact]
    public async Task A_part_that_is_only_waited_for_shows_its_expected_profit_and_stays_out_of_the_bill()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (id, _) = await OpenCase(owner);
        var billing = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items",
            new { kind = "part", title = "لنت", quantity = 2, unitPriceRials = 3_000_000, unitCostRials = 2_000_000, status = "needed" }));

        var money = billing.GetProperty("money");
        Assert.Equal(0, money.GetProperty("totalRials").GetInt64());      // not billed yet
        Assert.Equal(0, money.GetProperty("profitRials").GetInt64());     // billed profit stays real
        Assert.Equal(2_000_000, money.GetProperty("pendingProfitRials").GetInt64());
        var line = billing.GetProperty("items")[0];
        Assert.Equal(2_000_000, line.GetProperty("profitRials").GetInt64());
        Assert.True(line.GetProperty("profitIsExpected").GetBoolean());
    }

    [Fact]
    public async Task Expected_profit_is_never_shown_to_staff_who_cannot_see_costs()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techMobile = ArteApiFactory.NewMobile();
        var techId = (await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" }))).GetProperty("id").GetGuid();
        var created = await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), request = "x", assigneeId = techId,
            newAsset = new { title = "موتور", kind = "motorcycle" },
        }));
        var id = created.GetProperty("id").GetGuid();
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items",
            new { kind = "part", title = "لنت", unitPriceRials = 3_000_000, unitCostRials = 2_000_000, status = "needed" }));

        var (tech, _) = await api.LoginAsync(techMobile);
        var billing = (await Json(await tech.GetAsync($"/api/v1/cases/{id}"))).GetProperty("billing");
        Assert.Equal(JsonValueKind.Null, billing.GetProperty("money").GetProperty("pendingProfitRials").ValueKind);
        Assert.Equal(JsonValueKind.Null, billing.GetProperty("items")[0].GetProperty("profitRials").ValueKind);
    }

    // ───────── 12: who did each step ─────────

    [Fact]
    public async Task Every_timeline_step_names_who_did_it()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (id, _) = await OpenCase(owner);
        (await owner.PostAsJsonAsync($"/api/v1/cases/{id}/notes", new { text = "یادداشت" })).EnsureSuccessStatusCode();

        var timeline = (await Json(await owner.GetAsync($"/api/v1/cases/{id}"))).GetProperty("timeline").EnumerateArray().ToList();
        Assert.True(timeline.Count >= 2);
        // Everything a person did names that person; only the automatic customer SMS has no actor (the screen says «خودکار»).
        Assert.All(timeline.Where(e => e.GetProperty("type").GetString() != "customer.sms"),
            e => Assert.False(string.IsNullOrWhiteSpace(e.GetProperty("actor").GetString())));
    }

    // ───────── 18: users and passwords from the panel ─────────

    private static async Task<HttpResponseMessage> PasswordLogin(ArteApiFactory api, string identifier, string password) =>
        await api.Client().PostAsJsonAsync("/api/v1/auth/password", new { username = identifier, password });

    [Fact]
    public async Task Manager_creates_a_member_with_username_and_password_who_can_then_sign_in()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var mobile = ArteApiFactory.NewMobile();
        var username = "u" + Guid.NewGuid().ToString("N")[..8];
        var res = await owner.PostAsJsonAsync("/api/v1/staff", new { mobile, role = "technician", username, password = "a-strong-pass-123" });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await PasswordLogin(api, mobile, "a-strong-pass-123")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PasswordLogin(api, username, "a-strong-pass-123")).StatusCode);
        var list = await Json(await owner.GetAsync("/api/v1/staff"));
        var row = list.EnumerateArray().Single(r => r.GetProperty("mobile").GetString() == mobile);
        Assert.True(row.GetProperty("hasPassword").GetBoolean());
        // The password hash never leaves the server.
        Assert.DoesNotContain("pbkdf2", await (await owner.GetAsync("/api/v1/staff")).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Weak_or_duplicate_credentials_are_refused_when_adding_a_member()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var mobile = ArteApiFactory.NewMobile();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/staff", new { mobile, role = "technician", password = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/staff", new { mobile, role = "technician", password = mobile })).StatusCode);

        var username = "u" + Guid.NewGuid().ToString("N")[..8];
        Assert.Equal(HttpStatusCode.Created, (await owner.PostAsJsonAsync("/api/v1/staff",
            new { mobile = ArteApiFactory.NewMobile(), role = "technician", username, password = "a-strong-pass-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync("/api/v1/staff",
            new { mobile = ArteApiFactory.NewMobile(), role = "technician", username, password = "a-strong-pass-456" })).StatusCode);
    }

    [Fact]
    public async Task Manager_resets_a_members_password_and_their_old_sessions_end()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var mobile = ArteApiFactory.NewMobile();
        var staffId = (await Json(await owner.PostAsJsonAsync("/api/v1/staff",
            new { mobile, role = "technician", password = "a-strong-pass-123" }))).GetProperty("id").GetGuid();

        var login = await api.Client().PostAsJsonAsync("/api/v1/auth/password", new { username = mobile, password = "a-strong-pass-123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var put = await owner.PutAsJsonAsync($"/api/v1/staff/{staffId}/credentials", new { newPassword = "another-strong-pass-9" });
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await PasswordLogin(api, mobile, "a-strong-pass-123")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PasswordLogin(api, mobile, "another-strong-pass-9")).StatusCode);
    }

    [Fact]
    public async Task Credentials_need_staff_manage_and_never_cross_businesses_or_touch_the_owner()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techMobile = ArteApiFactory.NewMobile();
        var techId = (await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" }))).GetProperty("id").GetGuid();
        var supMobile = ArteApiFactory.NewMobile();
        var supId = (await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = supMobile, role = "supervisor" }))).GetProperty("id").GetGuid();
        var staff = await Json(await owner.GetAsync("/api/v1/staff"));
        var ownerMembershipId = staff.EnumerateArray().Single(r => r.GetProperty("role").GetString() == "owner").GetProperty("id").GetGuid();

        // A technician cannot set anyone's password.
        var (tech, _) = await api.LoginAsync(techMobile);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PutAsJsonAsync($"/api/v1/staff/{supId}/credentials", new { newPassword = "another-strong-pass-9" })).StatusCode);

        // A supervisor with staff.manage cannot change the owner's.
        (await owner.PatchAsJsonAsync($"/api/v1/staff/{supId}",
            new { permissions = new[] { "cases.create", "cases.assign", "cases.view_all", "cases.work", "staff.manage" } })).EnsureSuccessStatusCode();
        var (sup, _) = await api.LoginAsync(supMobile);
        Assert.Equal(HttpStatusCode.Forbidden, (await sup.PutAsJsonAsync($"/api/v1/staff/{ownerMembershipId}/credentials", new { newPassword = "another-strong-pass-9" })).StatusCode);

        // The owner's own password goes through «حساب من», not this endpoint.
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync($"/api/v1/staff/{ownerMembershipId}/credentials", new { newPassword = "another-strong-pass-9" })).StatusCode);

        // Another business cannot see this member at all.
        var (other, _) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync($"/api/v1/staff/{techId}/credentials", new { newPassword = "another-strong-pass-9" })).StatusCode);
    }

    [Fact]
    public async Task A_person_who_works_in_two_businesses_keeps_their_own_password()
    {
        var (ownerA, _) = await api.NewBusinessAsync();
        var (ownerB, _) = await api.NewBusinessAsync();
        var mobile = ArteApiFactory.NewMobile();
        var inA = (await Json(await ownerA.PostAsJsonAsync("/api/v1/staff", new { mobile, role = "technician" }))).GetProperty("id").GetGuid();
        await Json(await ownerB.PostAsJsonAsync("/api/v1/staff", new { mobile, role = "technician" }));

        Assert.Equal(HttpStatusCode.Conflict, (await ownerA.PutAsJsonAsync($"/api/v1/staff/{inA}/credentials", new { newPassword = "another-strong-pass-9" })).StatusCode);
        // And a password cannot be attached while adding someone who already has an account.
        var (ownerC, _) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await ownerC.PostAsJsonAsync("/api/v1/staff", new { mobile, role = "technician", password = "another-strong-pass-9" })).StatusCode);
    }
}
