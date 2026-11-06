using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Arte.Tests;

/// <summary>The shop logo: uploaded in business settings, shown on the customer page, private to its own business.</summary>
[Collection(ApiCollection.Name)]
public sealed class BusinessLogoTests(ArteApiFactory api)
{
    private static MultipartFormDataContent Png(int size = 64)
    {
        var bytes = new byte[size];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "logo.png");
        return form;
    }

    [Fact]
    public async Task Logo_is_saved_shown_on_the_customer_page_and_can_be_removed()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var id = (await (await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile = ArteApiFactory.NewMobile(), customerName = "م", reportedProblems = new[] { "روشن نمی‌شود" },
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        })).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var code = (await (await owner.GetAsync($"/api/v1/cases/{id}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("trackingCode").GetString()!;

        Assert.Equal(HttpStatusCode.NotFound, (await api.Client().GetAsync($"/api/v1/track/{code}/logo")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PostAsync("/api/v1/settings/business/logo", Png())).StatusCode);

        Assert.True((await (await owner.GetAsync("/api/v1/settings/business")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("hasLogo").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/settings/business/logo")).StatusCode);
        var track = await (await api.Client().GetAsync($"/api/v1/track/{code}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(track.GetProperty("shop").GetProperty("hasLogo").GetBoolean());
        var served = await api.Client().GetAsync($"/api/v1/track/{code}/logo");
        Assert.Equal("image/png", served.Content.Headers.ContentType?.MediaType);

        // Another business never sees it.
        var (other, _) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync("/api/v1/settings/business/logo")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync("/api/v1/settings/business/logo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client().GetAsync($"/api/v1/track/{code}/logo")).StatusCode);
    }

    [Fact]
    public async Task Logo_must_be_a_small_image()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var text = new MultipartFormDataContent { { new ByteArrayContent(new byte[100]), "file", "x.png" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/v1/settings/business/logo", text)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsync("/api/v1/settings/business/logo", Png(1_100_000))).StatusCode);
    }
}
