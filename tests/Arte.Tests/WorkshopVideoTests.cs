using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>Workshop plan 4: a short video next to the photos, for the shop only.</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkshopVideoTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static MultipartFormDataContent File(byte[] bytes, string type, string name, bool visible = true)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(type);
        form.Add(file, "file", name);
        form.Add(new StringContent(visible ? "true" : "false"), "visibleToCustomer");
        return form;
    }

    private static byte[] Mp4(int size = 64)
    {
        var bytes = new byte[size];
        "ftypisom"u8.CopyTo(bytes.AsSpan(4));
        return bytes;
    }

    [Fact]
    public async Task A_short_video_is_saved_with_the_case_and_never_shown_to_the_customer()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var id = (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "م", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        }))).GetProperty("id").GetGuid();
        var code = (await Json(await owner.GetAsync($"/api/v1/cases/{id}"))).GetProperty("trackingCode").GetString()!;

        var created = await Json(await owner.PostAsync($"/api/v1/cases/{id}/attachments", File(Mp4(), "video/mp4", "v.mp4")));
        var fileId = created.GetProperty("id").GetGuid();
        Assert.False(created.GetProperty("visibleToCustomer").GetBoolean());

        var photos = (await Json(await owner.GetAsync($"/api/v1/cases/{id}"))).GetProperty("photos").EnumerateArray().ToList();
        Assert.True(photos.Single().GetProperty("isVideo").GetBoolean());
        var download = await owner.GetAsync($"/api/v1/attachments/{fileId}");
        Assert.Equal("video/mp4", download.Content.Headers.ContentType?.MediaType);

        // Videos cannot be deleted for now.
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"/api/v1/attachments/{fileId}")).StatusCode);

        var track = await Json(await api.Client().GetAsync($"/api/v1/track/{code}"));
        Assert.Empty(track.GetProperty("photos").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client().GetAsync($"/api/v1/track/{code}/photos/{fileId}")).StatusCode);
    }

    [Fact]
    public async Task Other_files_and_oversized_files_are_refused()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var id = (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "م", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        }))).GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/v1/cases/{id}/attachments", File(new byte[200], "video/mp4", "fake.mp4"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync($"/api/v1/cases/{id}/attachments", File(Mp4(31 * 1024 * 1024), "video/mp4", "big.mp4"))).StatusCode);
    }
}
