using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Arte.Api.Cases;
using Arte.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arte.Tests;

/// <summary>A deleted video waits 3 days in the trash (visible in Settings, restorable), then the system removes it for good.</summary>
[Collection(ApiCollection.Name)]
public sealed class WorkshopVideoTrashTests(ArteApiFactory api)
{
    private static async Task<JsonElement> Json(HttpResponseMessage res)
    {
        Assert.True(res.IsSuccessStatusCode, $"{(int)res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return await res.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static MultipartFormDataContent Video()
    {
        var bytes = new byte[64];
        "ftypisom"u8.CopyTo(bytes.AsSpan(4));
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("video/mp4");
        form.Add(file, "file", "v.mp4");
        return form;
    }

    private async Task<(HttpClient Owner, Guid CaseId, Guid VideoId)> DeletedVideoAsync()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var caseId = (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "م", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        }))).GetProperty("id").GetGuid();
        var videoId = (await Json(await owner.PostAsync($"/api/v1/cases/{caseId}/attachments", Video()))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/v1/attachments/{videoId}")).StatusCode);
        return (owner, caseId, videoId);
    }

    [Fact]
    public async Task A_deleted_video_is_listed_in_the_trash_with_its_purge_date_and_can_be_watched_and_restored()
    {
        var (owner, caseId, videoId) = await DeletedVideoAsync();

        var list = (await Json(await owner.GetAsync("/api/v1/trash/videos"))).EnumerateArray().ToList();
        var row = Assert.Single(list);
        Assert.Equal(videoId, row.GetProperty("id").GetGuid());
        var days = (row.GetProperty("purgeAt").GetDateTimeOffset() - row.GetProperty("deletedAt").GetDateTimeOffset()).TotalDays;
        Assert.Equal(3, Math.Round(days));

        var file = await owner.GetAsync($"/api/v1/trash/videos/{videoId}/file");
        Assert.Equal("video/mp4", file.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync($"/api/v1/trash/videos/{videoId}/restore", null)).StatusCode);
        Assert.Empty((await Json(await owner.GetAsync("/api/v1/trash/videos"))).EnumerateArray());
        Assert.Single((await Json(await owner.GetAsync($"/api/v1/cases/{caseId}"))).GetProperty("photos").EnumerateArray());
    }

    [Fact]
    public async Task Only_a_settings_manager_sees_the_trash_and_another_business_sees_nothing()
    {
        var (owner, caseId, videoId) = await DeletedVideoAsync();
        var mobile = ArteApiFactory.NewMobile();
        (await owner.PostAsJsonAsync("/api/v1/staff", new { mobile, displayName = "شاگرد", role = "technician" })).EnsureSuccessStatusCode();
        var tech = (await api.LoginAsync(mobile)).Item1;
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.GetAsync("/api/v1/trash/videos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsync($"/api/v1/trash/videos/{videoId}/restore", null)).StatusCode);

        var (other, _) = await api.NewBusinessAsync();
        Assert.Empty((await Json(await other.GetAsync("/api/v1/trash/videos"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/trash/videos/{videoId}/file")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsync($"/api/v1/trash/videos/{videoId}/restore", null)).StatusCode);
        Assert.NotEqual(Guid.Empty, caseId);
    }

    [Fact]
    public async Task After_three_days_the_system_removes_the_video_for_good()
    {
        var (owner, _, videoId) = await DeletedVideoAsync();
        var purger = api.Services.GetRequiredService<VideoTrash>();

        // Two days later it is still there.
        await purger.RunOnceAsync(CancellationToken.None, DateTimeOffset.UtcNow.AddDays(2));
        Assert.Single((await Json(await owner.GetAsync("/api/v1/trash/videos"))).EnumerateArray());

        // Four days later it is gone: the row and the file.
        string path;
        await using (var scope = api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
            path = (await db.CaseAttachments.IgnoreQueryFilters([ArteDbContext.TenantFilter, ArteDbContext.SoftDeleteFilter]).SingleAsync(a => a.Id == videoId)).StoragePath;
        }
        var full = Path.Combine(Path.GetTempPath(), "arte-test-uploads", path);
        Assert.True(File.Exists(full));
        Assert.True(await purger.RunOnceAsync(CancellationToken.None, DateTimeOffset.UtcNow.AddDays(4)) >= 1);
        Assert.False(File.Exists(full));
        Assert.Empty((await Json(await owner.GetAsync("/api/v1/trash/videos"))).EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.PostAsync($"/api/v1/trash/videos/{videoId}/restore", null)).StatusCode);
    }
}
