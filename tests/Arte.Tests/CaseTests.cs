using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

[Collection(ApiCollection.Name)]
public sealed class CaseTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> Open(HttpClient client, object? extra = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["mobile"] = ArteApiFactory.NewMobile(),
            ["customerName"] = "علی رضایی",
            ["newAsset"] = new { title = "Honda CG 125", identifier = "12345" },
            ["request"] = "موتور هنگام گرم شدن خاموش می‌شود",
            ["odometerKm"] = 23400,
        };
        if (extra is not null)
            foreach (var p in extra.GetType().GetProperties()) body[p.Name] = p.GetValue(extra);
        var created = await Json(await client.PostAsJsonAsync("/api/v1/cases", body));
        return await Json(await client.GetAsync($"/api/v1/cases/{created.GetProperty("id").GetGuid()}"));
    }

    private static Guid Primary(JsonElement detail) =>
        detail.GetProperty("transitions").EnumerateArray().First(t => t.GetProperty("isPrimary").GetBoolean()).GetProperty("id").GetGuid();

    private static Guid TransitionTo(JsonElement detail, string stageName) =>
        detail.GetProperty("transitions").EnumerateArray()
            .First(t => t.GetProperty("toStage").GetProperty("name").GetString() == stageName).GetProperty("id").GetGuid();

    private static async Task<JsonElement> Run(HttpClient client, JsonElement detail, Guid transition, string? reason = null) =>
        await Json(await client.PostAsJsonAsync($"/api/v1/cases/{detail.GetProperty("id")}/transitions/{transition}", new { reason }));

    private static string StageKey(JsonElement detail) => detail.GetProperty("stage").GetProperty("key").GetString()!;

    [Fact]
    public async Task Quick_create_makes_customer_motorcycle_and_numbered_case_with_timeline()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var first = await Open(owner);
        var second = await Open(owner);

        Assert.Equal("received", StageKey(first));
        Assert.Equal(1, first.GetProperty("number").GetInt64());
        Assert.Equal(2, second.GetProperty("number").GetInt64());
        Assert.Equal("Honda CG 125", first.GetProperty("asset").GetProperty("title").GetString());
        Assert.Contains(first.GetProperty("timeline").EnumerateArray(), e => e.GetProperty("type").GetString() == "case.opened");
    }

    [Fact]
    public async Task Full_happy_path_through_the_motorcycle_workflow()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var c = await Open(owner);
        foreach (var expected in new[] { "diagnosing", "awaiting_approval", "repairing", "testing", "ready", "delivered" })
        {
            c = await Run(owner, c, Primary(c));
            Assert.Equal(expected, StageKey(c));
        }
        Assert.Equal("with_customer", c.GetProperty("custodyStatus").GetString());
        Assert.NotEqual(JsonValueKind.Null, c.GetProperty("closedAt").ValueKind);
    }

    [Fact]
    public async Task Assigned_technician_finishes_their_own_job_but_cannot_touch_others()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techMobile = ArteApiFactory.NewMobile();
        var techId = (await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, displayName = "حسین", role = "technician" })))
            .GetProperty("id").GetGuid();
        var mine = await Open(owner, new { assigneeId = techId });
        var other = await Open(owner);
        var (tech, _) = await api.LoginAsync(techMobile);

        var list = await Json(await tech.GetAsync("/api/v1/cases"));
        Assert.Single(list.EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await tech.GetAsync($"/api/v1/cases/{other.GetProperty("id")}")).StatusCode);

        var c = await Json(await tech.GetAsync($"/api/v1/cases/{mine.GetProperty("id")}"));
        c = await Run(tech, c, TransitionTo(c, "در حال تعمیر"));
        c = await Run(tech, c, Primary(c)); // پایان تعمیر → تست
        c = await Run(tech, c, Primary(c)); // آماده تحویل
        Assert.Equal("ready", StageKey(c));

        // Delivery needs cases.create: not offered to the technician, and refused if forced.
        Assert.Empty(c.GetProperty("transitions").EnumerateArray());
        var ownerView = await Json(await owner.GetAsync($"/api/v1/cases/{mine.GetProperty("id")}"));
        var deliver = Primary(ownerView);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await tech.PostAsJsonAsync($"/api/v1/cases/{mine.GetProperty("id")}/transitions/{deliver}", new { })).StatusCode);
    }

    [Fact]
    public async Task Stale_transition_is_rejected_with_409()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var c = await Open(owner);
        var start = Primary(c);
        await Run(owner, c, start);
        var again = await owner.PostAsJsonAsync($"/api/v1/cases/{c.GetProperty("id")}/transitions/{start}", new { });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Cancel_needs_a_reason()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var c = await Open(owner);
        var cancel = TransitionTo(c, "انصراف");
        var res = await owner.PostAsJsonAsync($"/api/v1/cases/{c.GetProperty("id")}/transitions/{cancel}", new { reason = "" });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        c = await Run(owner, c, cancel, "مشتری منصرف شد");
        Assert.Equal("cancelled", StageKey(c));
    }

    [Fact]
    public async Task Delivered_case_can_be_reopened_under_warranty_with_a_reason()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var c = await Open(owner);
        while (StageKey(c) != "delivered") c = await Run(owner, c, Primary(c));

        c = await Run(owner, c, TransitionTo(c, "پذیرش شد"), "همان صدا برگشت");
        Assert.Equal("received", StageKey(c));
        Assert.Equal(JsonValueKind.Null, c.GetProperty("closedAt").ValueKind);
        Assert.Contains(c.GetProperty("timeline").EnumerateArray(), e => e.GetProperty("type").GetString() == "case.reopened");
    }

    [Fact]
    public async Task New_case_can_point_to_a_recent_case_of_the_same_motorcycle()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var c = await Open(owner);
        while (StageKey(c) != "delivered") c = await Run(owner, c, Primary(c));
        var assetId = c.GetProperty("asset").GetProperty("id").GetGuid();
        var mobile = c.GetProperty("customer").GetProperty("mobile").GetString();

        var suggestion = await Json(await owner.GetAsync($"/api/v1/cases/suggest-parent?assetId={assetId}"));
        Assert.Equal(c.GetProperty("id").GetGuid(), suggestion.GetProperty("id").GetGuid());

        var comeback = await Json(await owner.PostAsJsonAsync("/api/v1/cases",
            new { mobile, assetId, request = "همان مشکل", parentCaseId = c.GetProperty("id").GetGuid() }));
        var detail = await Json(await owner.GetAsync($"/api/v1/cases/{comeback.GetProperty("id")}"));
        Assert.Equal(c.GetProperty("number").GetInt64(), detail.GetProperty("parentCase").GetProperty("number").GetInt64());
        Assert.Equal("comeback", detail.GetProperty("relation").GetString());
    }

    [Fact]
    public async Task Intake_with_services_fuel_body_and_vehicle_details_but_no_plate()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var created = await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(),
            newAsset = new { title = "پژو ۲۰۶", kind = "car", attributes = new Dictionary<string, string> { ["brand"] = "پژو", ["model"] = "۲۰۶", ["year"] = "1398" } },
            requestedServices = new[] { "تعویض روغن", "ترمز" },
            fuelLevel = 2, bodyStatus = "damaged", bodyNotes = "خط روی درب جلو",
        }));
        var c = await Json(await owner.GetAsync($"/api/v1/cases/{created.GetProperty("id")}"));
        Assert.Equal(2, c.GetProperty("requestedServices").GetArrayLength());
        Assert.Equal(2, c.GetProperty("fuelLevel").GetInt32());
        Assert.Equal("damaged", c.GetProperty("bodyStatus").GetString());
        Assert.Equal("پژو", c.GetProperty("asset").GetProperty("attributes").GetProperty("brand").GetString());

        // Work cannot start before the plate is entered…
        var start = await owner.PostAsJsonAsync($"/api/v1/cases/{c.GetProperty("id")}/transitions/{Primary(c)}", new { });
        Assert.Equal(HttpStatusCode.Conflict, start.StatusCode);
        Assert.Contains("plate_required", await start.Content.ReadAsStringAsync());

        // …and can once it is.
        var assetId = c.GetProperty("asset").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await owner.PatchAsJsonAsync($"/api/v1/assets/{assetId}", new { identifier = "12ب345-11" })).StatusCode);
        c = await Run(owner, c, Primary(c));
        Assert.Equal("diagnosing", StageKey(c));
    }

    [Fact]
    public async Task Waiting_for_parts_says_who_brings_them()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var c = await Open(owner);
        c = await Run(owner, c, Primary(c)); // عیب‌یابی
        var parts = TransitionTo(c, "منتظر قطعه");

        var missing = await owner.PostAsJsonAsync($"/api/v1/cases/{c.GetProperty("id")}/transitions/{parts}", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("wait_reason_required", await missing.Content.ReadAsStringAsync());

        c = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{c.GetProperty("id")}/transitions/{parts}", new { waitReason = "customer_parts" }));
        Assert.Equal("customer_parts", c.GetProperty("waitReason").GetString());

        // Leaving the waiting stage clears the reason.
        c = await Run(owner, c, Primary(c)); // قطعه رسید
        Assert.Equal(JsonValueKind.Null, c.GetProperty("waitReason").ValueKind);

        var inbox = await Json(await owner.GetAsync("/api/v1/inbox"));
        Assert.Contains(inbox.GetProperty("needsAction").EnumerateArray(),
            x => x.GetProperty("reasons").EnumerateArray().Any(r => r.GetProperty("code").GetString() == "unassigned"));
    }

    [Fact]
    public async Task Job_can_be_marked_stopped_and_shows_in_blocked_and_technician_inbox_is_their_own()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techMobile = ArteApiFactory.NewMobile();
        var techId = (await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" }))).GetProperty("id").GetGuid();
        var mine = await Open(owner, new { assigneeId = techId });
        await Open(owner);
        var (tech, _) = await api.LoginAsync(techMobile);

        var stopped = await Json(await tech.PostAsJsonAsync($"/api/v1/cases/{mine.GetProperty("id")}/wait", new { waitReason = "owner_decision" }));
        Assert.Equal("owner_decision", stopped.GetProperty("waitReason").GetString());

        var ownerInbox = await Json(await owner.GetAsync("/api/v1/inbox"));
        Assert.Contains(ownerInbox.GetProperty("blocked").EnumerateArray(), g => g.GetProperty("reason").GetString() == "owner_decision");

        var techInbox = await Json(await tech.GetAsync("/api/v1/inbox"));
        Assert.Single(techInbox.GetProperty("mine").EnumerateArray());
        Assert.Empty(techInbox.GetProperty("needsAction").EnumerateArray());
        Assert.Equal(HttpStatusCode.BadRequest, (await tech.PostAsJsonAsync($"/api/v1/cases/{mine.GetProperty("id")}/wait", new { waitReason = "nonsense" })).StatusCode);
    }

    [Fact]
    public async Task Plate_lookup_finds_the_vehicle_and_owner_within_the_business_only()
    {
        var (a, _) = await api.NewBusinessAsync();
        var (b, _) = await api.NewBusinessAsync();
        var plate = $"{Random.Shared.Next(10, 99)}ب{Random.Shared.Next(100, 999)}-{Random.Shared.Next(10, 99)}";
        var c = await Json(await a.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "صاحب", request = "x",
            newAsset = new { title = "پژو ۲۰۶", identifier = plate, kind = "car" },
        }));
        Assert.NotEqual(Guid.Empty, c.GetProperty("id").GetGuid());

        var hit = await Json(await a.GetAsync($"/api/v1/assets/lookup?identifier={Uri.EscapeDataString(plate)}"));
        Assert.Equal("صاحب", hit.GetProperty("customer").GetProperty("fullName").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/v1/assets/lookup?identifier={Uri.EscapeDataString(plate)}")).StatusCode);
    }

    [Fact]
    public async Task Intake_needs_a_service_or_a_description()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var res = await owner.PostAsJsonAsync("/api/v1/cases", new { mobile = ArteApiFactory.NewMobile() });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var bad = await owner.PostAsJsonAsync("/api/v1/cases", new { mobile = ArteApiFactory.NewMobile(), request = "x", fuelLevel = 9 });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task Asset_of_another_customer_is_refused()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var c = await Open(owner);
        var res = await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), assetId = c.GetProperty("asset").GetProperty("id").GetGuid(), request = "x",
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Cases_never_cross_businesses()
    {
        var (a, _) = await api.NewBusinessAsync();
        var (b, _) = await api.NewBusinessAsync();
        var c = await Open(a);
        var id = c.GetProperty("id");

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/v1/cases/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{Primary(c)}", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync($"/api/v1/cases/{id}/notes", new { text = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.DeleteAsync($"/api/v1/cases/{id}")).StatusCode);
        Assert.Equal(0, (await Json(await b.GetAsync("/api/v1/cases"))).GetArrayLength());
    }

    [Fact]
    public async Task Case_soft_delete_and_restore_and_notes_and_dashboard()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var c = await Open(owner);
        var id = c.GetProperty("id");

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsJsonAsync($"/api/v1/cases/{id}/notes", new { text = "قطعه سفارش داده شد" })).StatusCode);
        var dash = await Json(await owner.GetAsync("/api/v1/dashboard"));
        Assert.Equal(1, dash.GetProperty("open").GetInt32());
        Assert.Equal(1, dash.GetProperty("unassigned").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/cases/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"/api/v1/cases/{id}")).StatusCode);
        Assert.Equal(0, (await Json(await owner.GetAsync("/api/v1/dashboard"))).GetProperty("open").GetInt32());
        var restored = await Json(await owner.PostAsync($"/api/v1/cases/{id}/restore", null));
        Assert.Contains(restored.GetProperty("timeline").EnumerateArray(), e => e.GetProperty("type").GetString() == "case.note_added");
    }

    [Fact]
    public async Task Technician_cannot_open_assign_or_change_price()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var techMobile = ArteApiFactory.NewMobile();
        var techId = (await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" }))).GetProperty("id").GetGuid();
        var c = await Open(owner, new { assigneeId = techId });
        var (tech, _) = await api.LoginAsync(techMobile);
        var id = c.GetProperty("id");

        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync("/api/v1/cases", new { mobile = ArteApiFactory.NewMobile(), request = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync($"/api/v1/cases/{id}/assign", new { assigneeId = (Guid?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PatchAsJsonAsync($"/api/v1/cases/{id}", new { estimatedAmountRials = 1 })).StatusCode);
        // A promised time in Tehran local time is accepted (stored as UTC).
        Assert.Equal(HttpStatusCode.OK, (await owner.PatchAsJsonAsync($"/api/v1/cases/{id}",
            new { promisedAt = new DateTimeOffset(2026, 10, 3, 17, 0, 0, TimeSpan.FromHours(3.5)) })).StatusCode);
        // But they can record the diagnosis on their own job.
        Assert.Equal(HttpStatusCode.OK, (await tech.PatchAsJsonAsync($"/api/v1/cases/{id}", new { diagnosis = "شمع خراب" })).StatusCode);
    }
}
