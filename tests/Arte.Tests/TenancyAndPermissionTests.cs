using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arte.Core.Data;
using Arte.Core.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arte.Tests;

[Collection(ApiCollection.Name)]
public sealed class TenancyAndPermissionTests(ArteApiFactory api)
{
    [Fact]
    public void Every_tenant_owned_entity_has_a_query_filter()
    {
        using var scope = api.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();

        var unfiltered = db.Model.GetEntityTypes()
            .Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType))
            .Where(t => t.GetDeclaredQueryFilters().Count == 0)
            .Select(t => t.ClrType.Name)
            .ToList();

        Assert.Empty(unfiltered);
    }

    [Fact]
    public async Task Creating_a_business_needs_a_signed_in_user()
    {
        var res = await api.Client().PostAsJsonAsync("/api/v1/tenants", new { name = "x", ownerName = "علی رضایی" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task New_business_gets_the_motorcycle_workflow()
    {
        var (client, _) = await api.NewBusinessAsync();
        var wf = await client.GetFromJsonAsync<JsonElement>("/api/v1/workflow");
        var keys = wf.GetProperty("stages").EnumerateArray().Select(s => s.GetProperty("key").GetString()).ToList();
        // Customer approval is off by default; the master's final review is on.
        Assert.Equal(["received", "diagnosing", "awaiting_parts", "repairing", "review", "ready", "delivered", "cancelled"], keys);
    }

    [Fact]
    public async Task One_business_cannot_see_or_change_another_business_customers()
    {
        var (a, _) = await api.NewBusinessAsync();
        var (b, _) = await api.NewBusinessAsync();
        var mobile = ArteApiFactory.NewMobile();

        var created = await a.PostAsJsonAsync("/api/v1/customers", new { mobile, fullName = "علی رضایی" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/api/v1/customers/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PatchAsJsonAsync($"/api/v1/customers/{id}", new { fullName = "hacked" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync($"/api/v1/customers/{id}/assets", new { title = "x" })).StatusCode);
        var list = await b.GetFromJsonAsync<JsonElement>($"/api/v1/customers?q={mobile}");
        Assert.Equal(0, list.GetArrayLength());

        // Same mobile is a separate customer in business B.
        Assert.Equal(HttpStatusCode.Created, (await b.PostAsJsonAsync("/api/v1/customers", new { mobile })).StatusCode);
    }

    [Fact]
    public async Task Selecting_a_business_you_do_not_belong_to_is_refused()
    {
        var (_, tenantA) = await api.NewBusinessAsync();
        var (outsider, _) = await api.LoginAsync(ArteApiFactory.NewMobile());
        var res = await outsider.PostAsJsonAsync("/api/v1/auth/select-tenant", new { tenantId = tenantA });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Without_a_selected_business_tenant_endpoints_are_forbidden()
    {
        var (client, _) = await api.LoginAsync(ArteApiFactory.NewMobile());
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.Client().GetAsync("/api/v1/customers")).StatusCode);
    }

    [Fact]
    public async Task Technician_cannot_manage_staff_or_browse_customers()
    {
        var (owner, tenantId) = await api.NewBusinessAsync();
        var techMobile = ArteApiFactory.NewMobile();
        Assert.Equal(HttpStatusCode.Created,
            (await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, displayName = "شاگرد", role = "technician" })).StatusCode);

        var (tech, session) = await api.LoginAsync(techMobile);
        Assert.Equal(tenantId, session.GetProperty("tenantId").GetGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync("/api/v1/staff")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync("/api/v1/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync("/api/v1/customers", new { mobile = ArteApiFactory.NewMobile() })).StatusCode);
    }

    [Fact]
    public async Task Supervisor_can_register_customers_but_cannot_escalate_privileges()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var supMobile = ArteApiFactory.NewMobile();
        var added = await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = supMobile, role = "supervisor" });
        var supId = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        // Owner lets the supervisor manage staff, but not payments.
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/v1/staff/{supId}",
            new { permissions = new[] { "cases.create", "cases.assign", "cases.view_all", "cases.work", "staff.manage" } })).StatusCode);

        var (sup, _) = await api.LoginAsync(supMobile);
        Assert.Equal(HttpStatusCode.Created, (await sup.PostAsJsonAsync("/api/v1/customers", new { mobile = ArteApiFactory.NewMobile() })).StatusCode);

        var techMobile = ArteApiFactory.NewMobile();
        var tech = await sup.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" });
        var techId = (await tech.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Cannot hand out a permission the supervisor does not have.
        Assert.Equal(HttpStatusCode.Forbidden, (await sup.PatchAsJsonAsync($"/api/v1/staff/{techId}",
            new { permissions = new[] { "cases.work", "payments.record" } })).StatusCode);
        // Cannot change their own permissions.
        Assert.Equal(HttpStatusCode.Forbidden, (await sup.PatchAsJsonAsync($"/api/v1/staff/{supId}",
            new { permissions = new[] { "payments.record" } })).StatusCode);
        // Cannot touch the owner.
        var staff = await owner.GetFromJsonAsync<JsonElement>("/api/v1/staff");
        var ownerId = staff.EnumerateArray().First(s => s.GetProperty("role").GetString() == "owner").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await sup.PatchAsJsonAsync($"/api/v1/staff/{ownerId}", new { isActive = false })).StatusCode);
    }

    [Fact]
    public async Task Deactivated_staff_lose_access_immediately()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var supMobile = ArteApiFactory.NewMobile();
        var added = await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = supMobile, role = "supervisor" });
        var supId = (await added.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var (sup, _) = await api.LoginAsync(supMobile);
        Assert.Equal(HttpStatusCode.OK, (await sup.GetAsync("/api/v1/customers")).StatusCode);

        await owner.PatchAsJsonAsync($"/api/v1/staff/{supId}", new { isActive = false });

        // Same, still unexpired access token.
        Assert.Equal(HttpStatusCode.Unauthorized, (await sup.GetAsync("/api/v1/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await sup.PostAsync("/api/v1/auth/refresh", null)).StatusCode);
    }

    [Fact]
    public async Task Search_treats_like_wildcards_literally()
    {
        var (client, _) = await api.NewBusinessAsync();
        await client.PostAsJsonAsync("/api/v1/customers", new { mobile = ArteApiFactory.NewMobile(), fullName = "حسن" });
        var res = await client.GetFromJsonAsync<JsonElement>("/api/v1/customers?q=%25");
        Assert.Equal(0, res.GetArrayLength());
    }

    [Fact]
    public async Task Writes_to_another_tenant_are_refused_even_when_filters_are_bypassed()
    {
        var (a, tenantA) = await api.NewBusinessAsync();
        var (_, tenantB) = await api.NewBusinessAsync();
        var created = await a.PostAsJsonAsync("/api/v1/customers", new { mobile = ArteApiFactory.NewMobile() });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using var scope = api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenantB);
        var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
        var foreign = await db.Customers.IgnoreQueryFilters().SingleAsync(c => c.Id == id);
        foreign.FullName = "tampered";

        await Assert.ThrowsAsync<TenantViolationException>(() => db.SaveChangesAsync());
        Assert.NotEqual(tenantA, tenantB);
    }
}
