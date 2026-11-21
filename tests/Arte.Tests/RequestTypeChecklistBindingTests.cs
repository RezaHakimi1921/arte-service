using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

[Collection(ApiCollection.Name)]
public sealed class RequestTypeChecklistBindingTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task Bindings_default_to_motorcycle_and_can_be_set_to_none()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (other, _) = await api.NewBusinessAsync();
        var techMobile = ArteApiFactory.NewMobile();
        await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, displayName = "کارگر", role = "technician" });
        var (tech, _) = await api.LoginAsync(techMobile);

        var got = await Json(await owner.GetAsync("/api/v1/request-type-checklist-bindings"));
        Assert.Equal(3, got.GetProperty("bindings").GetArrayLength());
        Assert.All(got.GetProperty("bindings").EnumerateArray(), b => Assert.Equal("motorcycle", b.GetProperty("source").GetString()));

        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PutAsJsonAsync("/api/v1/request-type-checklist-bindings", new
        {
            bindings = new[]
            {
                new { requestTypeKey = "wrenching", source = "none" },
                new { requestTypeKey = "diagnosis", source = "none" },
                new { requestTypeKey = "repair", source = "none" },
            },
        })).StatusCode);

        (await owner.PutAsJsonAsync("/api/v1/request-type-checklist-bindings", new
        {
            bindings = new[]
            {
                new { requestTypeKey = "wrenching", source = "none" },
                new { requestTypeKey = "diagnosis", source = "motorcycle" },
                new { requestTypeKey = "repair", source = "motorcycle" },
            },
        })).EnsureSuccessStatusCode();

        var again = await Json(await owner.GetAsync("/api/v1/request-type-checklist-bindings"));
        var wrench = again.GetProperty("bindings").EnumerateArray().First(b => b.GetProperty("requestTypeKey").GetString() == "wrenching");
        Assert.Equal("none", wrench.GetProperty("source").GetString());

        // Other tenant cannot see these rows as theirs changing (they get their own defaults).
        var foreign = await Json(await other.GetAsync("/api/v1/request-type-checklist-bindings"));
        Assert.All(foreign.GetProperty("bindings").EnumerateArray(), b => Assert.Equal("motorcycle", b.GetProperty("source").GetString()));
    }

    [Fact]
    public async Task Create_snapshots_checklist_without_blocking_on_incomplete_required_rows()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var a = (await Json(await owner.PostAsJsonAsync("/api/v1/checklist-templates/automatic/items", new { title = "بررسی ترمز" }))).GetProperty("id").GetGuid();
        await owner.PostAsJsonAsync("/api/v1/checklist-templates/automatic/items", new { title = "بررسی چراغ", isRequired = false });

        var created = await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(),
            request = "آچارکشی",
            requestTypes = new[] { "wrenching" },
            newAsset = new { title = "کلیک", kind = "motorcycle" },
            motorType = "automatic",
            checklist = Array.Empty<object>(),
        }));
        var detail = await Json(await owner.GetAsync($"/api/v1/cases/{created.GetProperty("id")}"));
        var list = detail.GetProperty("checklist").EnumerateArray().ToList();
        Assert.Equal(2, list.Count);
        Assert.False(list[0].GetProperty("isChecked").GetBoolean());
        Assert.Contains(detail.GetProperty("timeline").EnumerateArray(), e => e.GetProperty("type").GetString() == "case.opened");

        // Only «سایر» → no checklist snapshot even with motorType.
        var other = await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(),
            request = "تعویض چرخ",
            requestTypes = new[] { "other" },
            motorType = "automatic",
            checklist = new[] { new { itemId = a, isChecked = true } },
        }));
        var otherDetail = await Json(await owner.GetAsync($"/api/v1/cases/{other.GetProperty("id")}"));
        Assert.Empty(otherDetail.GetProperty("checklist").EnumerateArray());

        // Binding none for wrenching → no rows; motorType not required.
        (await owner.PutAsJsonAsync("/api/v1/request-type-checklist-bindings", new
        {
            bindings = new[]
            {
                new { requestTypeKey = "wrenching", source = "none" },
                new { requestTypeKey = "diagnosis", source = "none" },
                new { requestTypeKey = "repair", source = "none" },
            },
        })).EnsureSuccessStatusCode();
        var bare = await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(),
            request = "سرویس",
            requestTypes = new[] { "wrenching" },
        }));
        var bareDetail = await Json(await owner.GetAsync($"/api/v1/cases/{bare.GetProperty("id")}"));
        Assert.Empty(bareDetail.GetProperty("checklist").EnumerateArray());
    }

    [Fact]
    public async Task Bindable_type_with_motorcycle_binding_requires_motor_type()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var res = await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(),
            request = "عیب‌یابی",
            requestTypes = new[] { "diagnosis" },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("motorType", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Incomplete_required_checklist_blocks_start_of_work_but_not_cancel()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var a = (await Json(await owner.PostAsJsonAsync("/api/v1/checklist-templates/geared/items", new { title = "آینه" }))).GetProperty("id").GetGuid();
        await owner.PostAsJsonAsync("/api/v1/checklist-templates/geared/items", new { title = "چراغ", isRequired = false });

        var created = await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(),
            request = "آچارکشی",
            requestTypes = new[] { "wrenching" },
            motorType = "geared",
            checklist = Array.Empty<object>(),
        }));
        var id = created.GetProperty("id").GetGuid();
        var detail = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        Assert.Equal("received", detail.GetProperty("stage").GetProperty("key").GetString());

        var start = detail.GetProperty("transitions").EnumerateArray()
            .First(t => t.GetProperty("toStage").GetProperty("name").GetString()!.Contains("عیب")
                || t.GetProperty("isPrimary").GetBoolean()).GetProperty("id").GetGuid();
        var blocked = await owner.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{start}", new { });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var body = await blocked.Content.ReadAsStringAsync();
        Assert.Contains("آینه", body);
        Assert.Contains("checklist_incomplete", body);

        var cancel = detail.GetProperty("transitions").EnumerateArray()
            .First(t => t.GetProperty("toStage").GetProperty("name").GetString()!.Contains("انصراف")
                || t.GetProperty("label").GetString() == "انصراف").GetProperty("id").GetGuid();
        // Cancel must still work with an incomplete checklist.
        var cancelled = await owner.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{cancel}", new { reason = "مشتری منصرف شد" });
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        // Fresh case: tick required row, then start work succeeds.
        var created2 = await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(),
            request = "آچارکشی",
            requestTypes = new[] { "wrenching" },
            motorType = "geared",
            checklist = new[] { new { itemId = a, isChecked = true } },
        }));
        var d2 = await Json(await owner.GetAsync($"/api/v1/cases/{created2.GetProperty("id")}"));
        var start2 = d2.GetProperty("transitions").EnumerateArray().First(t => t.GetProperty("isPrimary").GetBoolean()).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync($"/api/v1/cases/{created2.GetProperty("id")}/transitions/{start2}", new { })).StatusCode);
    }
}
