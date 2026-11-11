using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arte.Api.Sms;
using Arte.Api.Sms.SmartX;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Arte.Tests;

public sealed class SmsTextTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(70, 1)]
    [InlineData(71, 2)]
    [InlineData(134, 2)]
    [InlineData(135, 3)]
    public void Persian_text_counts_70_then_67_per_part(int length, int parts) =>
        Assert.Equal(parts, SmsParts.Count(new string('س', length)));

    [Theory]
    [InlineData(160, 1)]
    [InlineData(161, 2)]
    [InlineData(306, 2)]
    [InlineData(307, 3)]
    public void Latin_text_counts_160_then_153_per_part(int length, int parts) =>
        Assert.Equal(parts, SmsParts.Count(new string('a', length)));

    [Fact]
    public void One_persian_letter_makes_the_whole_text_ucs2()
    {
        // 100 Latin letters are one GSM part, but with a single Persian letter the 70-unit rule applies.
        Assert.Equal(1, SmsParts.Count(new string('a', 100)));
        Assert.Equal(2, SmsParts.Count(new string('a', 99) + "س"));
    }

    [Fact]
    public void Default_texts_are_valid_and_the_one_with_a_link_needs_the_link_tag()
    {
        foreach (var d in SmsTemplates.Branch) Assert.Null(SmsTemplates.Validate(d.Key, d.Default));
        Assert.NotNull(SmsTemplates.Validate(MessageKeys.CaseReady, "آماده است"));
        Assert.NotNull(SmsTemplates.Validate(MessageKeys.CaseReady, "{{CustomerLink}} {{Nope}}"));
        Assert.NotNull(SmsTemplates.Validate(MessageKeys.CaseReady, "{{CustomerLink}} {{Code}}"));   // the login code is not a branch tag
        Assert.NotNull(SmsTemplates.Validate(MessageKeys.CaseReady, "  "));
        Assert.NotNull(SmsTemplates.Validate(MessageKeys.AuthOtp, "x"));
        Assert.NotNull(SmsTemplates.Validate(MessageKeys.CaseReady, "{{CustomerLink}}" + new string('x', SmsTemplates.MaxLength)));
    }

    [Fact]
    public void Tags_are_filled_from_the_message_tokens()
    {
        var tokens = new Dictionary<string, string> { ["name"] = "رضا", ["shop"] = "مکانیک موتور", ["vehicle"] = "موتور هوندا", ["code"] = "abc234" };
        var v = SmsTemplateService.Values(MessageKeys.CaseReady, tokens, new Dictionary<string, string> { ["CaseCode"] = "CASE-7" }, "service.artepersia.com/");
        Assert.Equal("رضا | مکانیک موتور | service.artepersia.com/t/abc234 | CASE-7",
            SmsTags.Fill("{{CustomerName}} | {{ WorkshopName }} | {{CustomerLink}} | {{CaseCode}}", v));
        Assert.Equal("service.artepersia.com/s/abc234", SmsTemplateService.Values(MessageKeys.SurveyRequest, tokens, null, "service.artepersia.com")[SmsTags.CustomerLink]);
    }
}

[Collection(ApiCollection.Name)]
public sealed class SmsWalletTests(ArteApiFactory api)
{
    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow => now; }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://staging-api.smartx.ir") };
    }

    private sealed class Handler(Func<HttpResponseMessage> directSend) : HttpMessageHandler
    {
        public readonly List<string> Bodies = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (req.RequestUri!.AbsolutePath == "/connect/token")
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"access_token\":\"t\",\"expires_in\":3600}", Encoding.UTF8, "application/json") };
            Bodies.Add(await req.Content!.ReadAsStringAsync(ct));
            return directSend();
        }
    }

    private static readonly DateTimeOffset Daytime = new(2026, 11, 7, 6, 30, 0, TimeSpan.Zero);    // 10:00 Tehran
    private static readonly DateTimeOffset Night = new(2026, 11, 7, 21, 0, 0, TimeSpan.Zero);      // 00:30 Tehran

    private sealed record Rig(SmartXSmsProvider Provider, SmsWalletService Wallet, SmsTemplateService Templates, Handler Http, AsyncServiceScope Scope);

    private Rig NewRig(Func<HttpResponseMessage>? directSend = null, DateTimeOffset? now = null, Action<SmartXSmsOptions>? tweak = null)
    {
        var scope = api.Services.CreateAsyncScope();
        var wallet = scope.ServiceProvider.GetRequiredService<SmsWalletService>();
        var templates = scope.ServiceProvider.GetRequiredService<SmsTemplateService>();
        var http = new Handler(directSend ?? (() => new HttpResponseMessage(HttpStatusCode.OK)));
        var o = new SmartXSmsOptions { ClientId = "id", ClientSecret = "secret", TenantId = "1", SenderNumber = "989982008609", MaxAttempts = 1, RetryBaseDelaySeconds = 1 };
        tweak?.Invoke(o);
        var opts = Options.Create(o);
        var factory = new Factory(http);
        var client = new SmartXSmsClient(factory, new SmartXTokenProvider(factory, opts), new InMemorySmsBatchLog(), opts, NullLogger<SmartXSmsClient>.Instance);
        var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
        var provider = new SmartXSmsProvider(client, wallet, templates, db, new FixedClock(now ?? Daytime), opts, NullLogger<SmartXSmsProvider>.Instance);
        return new Rig(provider, wallet, templates, http, scope);
    }

    /// <summary>Central balance = what the branches hold + <paramref name="free"/>; the given branch holds <paramref name="branchParts"/>.</summary>
    private static async Task<Guid> FundAsync(Rig rig, Guid tenantId, long branchParts, long free = 1_000)
    {
        var st = await rig.Wallet.StatusAsync(default);
        var current = await rig.Wallet.BranchBalanceAsync(tenantId, default);
        await rig.Wallet.SyncCentralAsync(st.CommittedParts + branchParts - current + free, "test", null, default);
        if (branchParts != current) await rig.Wallet.CreditBranchAsync(tenantId, branchParts - current, null, "test", null, default);
        return tenantId;
    }

    private static SmsRequest Case(Guid tenantId, string key = MessageKeys.CaseReady) =>
        new("09121234567", key,
            new Dictionary<string, string> { ["name"] = "رضا", ["shop"] = "مکانیک موتور", ["vehicle"] = "موتور هوندا", ["code"] = "abc234" },
            tenantId, new Dictionary<string, string> { ["CaseCode"] = "CASE-5" });

    private async Task<HttpClient> AdminAsync()
    {
        var mobile = ArteApiFactory.NewMobile();
        var (client, _) = await api.LoginAsync(mobile);
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ArteDbContext>().Users.Where(u => u.Mobile == mobile)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsPlatformAdmin, true));
        return client;
    }

    // ───────── spending rules ─────────

    [Fact]
    public async Task A_branch_message_takes_its_parts_from_the_branch_and_the_central_balance()
    {
        var (_, tenantId) = await api.NewBusinessAsync();
        using var rig = Wrap(NewRig());
        await FundAsync(rig.Inner, tenantId, 10);
        var before = await rig.Inner.Wallet.StatusAsync(default);

        var res = await rig.Inner.Provider.SendAsync(Case(tenantId), default);

        Assert.True(res.Accepted, res.Error);
        var after = await rig.Inner.Wallet.StatusAsync(default);
        var parts = SmsParts.Count(Sent(rig.Inner));
        Assert.Equal(10 - parts, await rig.Inner.Wallet.BranchBalanceAsync(tenantId, default));
        Assert.Equal(before.CentralParts - parts, after.CentralParts);
        Assert.Equal(before.HeadroomParts, after.HeadroomParts);   // spending never changes the free amount
        Assert.Contains("همراه گرامی", Sent(rig.Inner));
        Assert.Contains("service.artepersia.com/t/abc234", Sent(rig.Inner));
    }

    [Fact]
    public async Task The_login_code_never_goes_through_smartx_and_costs_no_credit()
    {
        var (_, tenantId) = await api.NewBusinessAsync();
        using var rig = Wrap(NewRig());
        await FundAsync(rig.Inner, tenantId, 10);
        var before = await rig.Inner.Wallet.StatusAsync(default);

        var res = await rig.Inner.Provider.SendAsync(
            new SmsRequest("09121234567", MessageKeys.AuthOtp, new Dictionary<string, string> { ["code"] = "123456" }, tenantId), default);

        Assert.False(res.Accepted);
        Assert.Equal(SmartXSmsProvider.OtpNotViaSmartX, res.Error);
        Assert.Empty(rig.Inner.Http.Bodies);
        Assert.Equal(10, await rig.Inner.Wallet.BranchBalanceAsync(tenantId, default));
        Assert.Equal(before.CentralParts, (await rig.Inner.Wallet.StatusAsync(default)).CentralParts);
    }

    [Fact]
    public async Task A_branch_without_credit_is_soft_skipped_and_nothing_is_sent()
    {
        var (_, tenantId) = await api.NewBusinessAsync();
        using var rig = Wrap(NewRig());
        await FundAsync(rig.Inner, tenantId, 0);

        var res = await rig.Inner.Provider.SendAsync(Case(tenantId), default);
        Assert.False(res.Accepted);
        Assert.Equal(SmartXSmsProvider.SkippedNoCredit, res.Error);
        Assert.Empty(rig.Inner.Http.Bodies);
    }

    [Fact]
    public async Task An_empty_central_balance_refuses_even_the_login_code_and_returns_the_branch_parts()
    {
        var (_, tenantId) = await api.NewBusinessAsync();
        using var rig = Wrap(NewRig());
        await FundAsync(rig.Inner, tenantId, 5, free: 0);
        await rig.Inner.Wallet.SyncCentralAsync(0, "test", null, default);   // Arte has nothing left at SmartX

        var res = await rig.Inner.Provider.SendAsync(Case(tenantId), default);

        Assert.False(res.Accepted);
        Assert.Equal(SmsWalletService.CentralInsufficient, res.Error);
        Assert.Equal(5, await rig.Inner.Wallet.BranchBalanceAsync(tenantId, default));   // the branch deduction was rolled back
    }

    [Fact]
    public async Task A_message_smartx_does_not_accept_gives_its_parts_back()
    {
        var (_, tenantId) = await api.NewBusinessAsync();
        using var rig = Wrap(NewRig(() => new HttpResponseMessage(HttpStatusCode.BadRequest)));
        await FundAsync(rig.Inner, tenantId, 10);
        var before = await rig.Inner.Wallet.StatusAsync(default);

        var res = await rig.Inner.Provider.SendAsync(Case(tenantId), default);

        Assert.False(res.Accepted);
        Assert.Equal(SmartXSmsProvider.SendFailed, res.Error);
        Assert.Equal(10, await rig.Inner.Wallet.BranchBalanceAsync(tenantId, default));
        Assert.Equal(before.CentralParts, (await rig.Inner.Wallet.StatusAsync(default)).CentralParts);
    }

    [Fact]
    public async Task Branch_messages_wait_for_the_morning()
    {
        var (_, tenantId) = await api.NewBusinessAsync();
        using var rig = Wrap(NewRig(now: Night));
        await FundAsync(rig.Inner, tenantId, 10);

        var res = await rig.Inner.Provider.SendAsync(Case(tenantId), default);
        Assert.Equal(SmartXSmsProvider.OutsideHours, res.Error);
        Assert.Equal(10, await rig.Inner.Wallet.BranchBalanceAsync(tenantId, default));
        Assert.Empty(rig.Inner.Http.Bodies);
    }

    [Fact]
    public async Task Only_the_approved_text_of_this_branch_is_sent()
    {
        var (_, tenantId) = await api.NewBusinessAsync();
        var (_, other) = await api.NewBusinessAsync();
        using var rig = Wrap(NewRig());
        await FundAsync(rig.Inner, tenantId, 50);

        Assert.Null(await rig.Inner.Templates.SaveDraftAsync(tenantId, MessageKeys.CaseReady, "{{WorkshopName}}: {{CustomerName}} جان، {{Vehicle}} آماده است. {{CustomerLink}}", default));
        await rig.Inner.Provider.SendAsync(Case(tenantId), default);
        Assert.DoesNotContain("جان", Sent(rig.Inner));   // a draft is not sent: the system text is

        Assert.Null(await rig.Inner.Templates.ApproveAsync(tenantId, MessageKeys.CaseReady, Guid.NewGuid(), default));
        await rig.Inner.Provider.SendAsync(Case(tenantId), default);
        Assert.Contains("رضا جان", Sent(rig.Inner));

        await FundAsync(rig.Inner, other, 5);
        await rig.Inner.Provider.SendAsync(Case(other), default);
        Assert.DoesNotContain("جان", Sent(rig.Inner));   // another branch keeps the system text

        await rig.Inner.Templates.ResetAsync(tenantId, MessageKeys.CaseReady, default);
        await rig.Inner.Provider.SendAsync(Case(tenantId), default);
        Assert.DoesNotContain("جان", Sent(rig.Inner));
    }

    [Fact]
    public async Task When_approval_is_required_an_unapproved_text_is_not_sent()
    {
        var (_, tenantId) = await api.NewBusinessAsync();
        using var rig = Wrap(NewRig(tweak: o => o.RequireApprovedTemplate = true));
        await FundAsync(rig.Inner, tenantId, 10);

        var res = await rig.Inner.Provider.SendAsync(Case(tenantId), default);

        Assert.Equal(SmartXSmsProvider.NotApproved, res.Error);
        Assert.Equal(10, await rig.Inner.Wallet.BranchBalanceAsync(tenantId, default));
    }

    [Fact]
    public async Task Concurrent_messages_cannot_spend_the_same_parts_twice()
    {
        var (_, tenantId) = await api.NewBusinessAsync();
        using var rig = Wrap(NewRig());
        var one = SmsParts.Count(SmsTags.Fill(SmsTemplates.DefaultFor(MessageKeys.CaseReady), SmsTags.Samples));
        await FundAsync(rig.Inner, tenantId, one * 3);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var scope = api.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<SmsWalletService>().ReserveAsync(tenantId, one, MessageKeys.CaseReady, default);
        }));

        Assert.Equal(3, results.Count(r => r.Reservation is not null));
        Assert.Equal(0, await rig.Inner.Wallet.BranchBalanceAsync(tenantId, default));
    }

    // ───────── admin API ─────────

    [Fact]
    public async Task Only_the_platform_admin_reaches_the_sms_admin()
    {
        var (owner, tenantId) = await api.NewBusinessAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/admin/sms")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync($"/api/v1/admin/sms/businesses/{tenantId}/credit", new { parts = 5 })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/v1/admin/sms/businesses/{tenantId}/templates")).StatusCode);
    }

    [Fact]
    public async Task A_branch_cannot_be_given_more_than_the_central_balance_leaves_free()
    {
        var admin = await AdminAsync();
        var (_, a) = await api.NewBusinessAsync();
        var (_, b) = await api.NewBusinessAsync();
        var status = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/sms")).GetProperty("status");
        var committed = status.GetProperty("committedParts").GetInt64();
        (await admin.PostAsJsonAsync("/api/v1/admin/sms/central/sync", new { parts = committed + 100, note = "پنل اسمارت" })).EnsureSuccessStatusCode();

        (await admin.PostAsJsonAsync($"/api/v1/admin/sms/businesses/{a}/credit", new { parts = 100 })).EnsureSuccessStatusCode();

        var tooMuch = await admin.PostAsJsonAsync($"/api/v1/admin/sms/businesses/{b}/credit", new { parts = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, tooMuch.StatusCode);
        Assert.Equal("exceeds_central", (await tooMuch.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        // Taking credit back frees it for another branch.
        (await admin.PostAsJsonAsync($"/api/v1/admin/sms/businesses/{a}/credit", new { parts = -40 })).EnsureSuccessStatusCode();
        (await admin.PostAsJsonAsync($"/api/v1/admin/sms/businesses/{b}/credit", new { parts = 40 })).EnsureSuccessStatusCode();

        // …and no branch can be taken below zero.
        var below = await admin.PostAsJsonAsync($"/api/v1/admin/sms/businesses/{b}/credit", new { parts = -41 });
        Assert.Equal(HttpStatusCode.BadRequest, below.StatusCode);
    }

    [Fact]
    public async Task An_amount_is_turned_into_whole_parts_at_the_sale_price_and_the_old_ledger_is_kept()
    {
        var admin = await AdminAsync();
        var (_, tenantId) = await api.NewBusinessAsync();
        await admin.PutAsJsonAsync("/api/v1/admin/sms/prices", new { purchasePricePerPartRial = 1000, salePricePerPartRial = 2190 });
        var committed = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/sms")).GetProperty("status").GetProperty("committedParts").GetInt64();
        await admin.PostAsJsonAsync("/api/v1/admin/sms/central/sync", new { parts = committed + 20_000 });

        // 2,000,000 toman = 20,000,000 rials ÷ 2,190 = 9,132 parts.
        var res = await admin.PostAsJsonAsync($"/api/v1/admin/sms/businesses/{tenantId}/credit", new { amountRial = 20_000_000, note = "مکانیک موتور" });
        res.EnsureSuccessStatusCode();
        Assert.Equal(9132, (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("parts").GetInt64());

        var detail = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/admin/sms/businesses/{tenantId}");
        var entry = detail.GetProperty("ledger")[0];
        Assert.Equal("credit", entry.GetProperty("kind").GetString());
        Assert.Equal(20_000_000, entry.GetProperty("amountRial").GetInt64());
    }

    [Fact]
    public async Task The_sale_price_can_not_be_below_the_purchase_price()
    {
        var admin = await AdminAsync();
        var res = await admin.PutAsJsonAsync("/api/v1/admin/sms/prices", new { purchasePricePerPartRial = 2000, salePricePerPartRial = 1000 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Unknown_business_is_404_and_the_branch_sees_only_its_own_balance()
    {
        var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/admin/sms/businesses/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/admin/sms/businesses/{Guid.NewGuid()}/templates")).StatusCode);

        var (owner, tenantId) = await api.NewBusinessAsync();
        var committed = (await admin.GetFromJsonAsync<JsonElement>("/api/v1/admin/sms")).GetProperty("status").GetProperty("committedParts").GetInt64();
        await admin.PostAsJsonAsync("/api/v1/admin/sms/central/sync", new { parts = committed + 7 });
        await admin.PostAsJsonAsync($"/api/v1/admin/sms/businesses/{tenantId}/credit", new { parts = 7 });
        var wallet = await owner.GetFromJsonAsync<JsonElement>("/api/v1/sms/wallet");
        Assert.Equal(7, wallet.GetProperty("parts").GetInt64());
        Assert.True(wallet.GetProperty("salePricePerPartRial").GetInt64() > 0);
        Assert.Equal(7 * wallet.GetProperty("salePricePerPartRial").GetInt64(), wallet.GetProperty("valueRial").GetInt64());
    }

    [Fact]
    public async Task Template_draft_approve_reset_through_the_admin_api()
    {
        var admin = await AdminAsync();
        var (_, tenantId) = await api.NewBusinessAsync();
        var url = $"/api/v1/admin/sms/businesses/{tenantId}/templates";

        var bad = await admin.PutAsJsonAsync($"{url}/case.ready", new { text = "بدون لینک" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        (await admin.PutAsJsonAsync($"{url}/case.ready", new { text = "{{WorkshopName}}: آماده است {{CustomerLink}}" })).EnsureSuccessStatusCode();
        Assert.Equal("draft", Status(await admin.GetFromJsonAsync<JsonElement>(url), "case.ready"));

        (await admin.PostAsync($"{url}/case.ready/approve", null)).EnsureSuccessStatusCode();
        Assert.Equal("approved", Status(await admin.GetFromJsonAsync<JsonElement>(url), "case.ready"));

        (await admin.PostAsync($"{url}/case.ready/reset", null)).EnsureSuccessStatusCode();
        Assert.Equal("default", Status(await admin.GetFromJsonAsync<JsonElement>(url), "case.ready"));

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"{url}/case.ready/approve", null)).StatusCode);   // nothing to approve
    }

    // ───────── routing ─────────

    [Fact]
    public async Task Listed_keys_go_to_smartx_and_the_rest_stay_on_the_old_provider()
    {
        var legacy = new Recorder("legacy");
        var smartx = new Recorder("smartx");
        var router = new RoutingSmsProvider(legacy, smartx, new HashSet<string> { MessageKeys.CaseOpened }, smartxAll: false);

        await router.SendAsync(new SmsRequest("09121234567", MessageKeys.CaseOpened, new Dictionary<string, string>()), default);
        await router.SendAsync(new SmsRequest("09121234567", MessageKeys.CaseReady, new Dictionary<string, string>()), default);
        await router.SendAsync(new SmsRequest("09121234567", MessageKeys.AuthOtp, new Dictionary<string, string>()), default);

        Assert.Equal([MessageKeys.CaseOpened], smartx.Keys);
        Assert.Equal([MessageKeys.CaseReady, MessageKeys.AuthOtp], legacy.Keys);
    }

    [Fact]
    public async Task With_smartx_all_every_message_but_the_login_code_goes_to_smartx()
    {
        var legacy = new Recorder("legacy");
        var smartx = new Recorder("smartx");
        var router = new RoutingSmsProvider(legacy, smartx, new HashSet<string>(), smartxAll: true);

        foreach (var key in new[] { MessageKeys.CaseOpened, MessageKeys.CaseReady, MessageKeys.CaseDelivered, MessageKeys.CaseLink, MessageKeys.SurveyRequest, MessageKeys.AuthOtp })
            await router.SendAsync(new SmsRequest("09121234567", key, new Dictionary<string, string>()), default);

        Assert.Equal([MessageKeys.CaseOpened, MessageKeys.CaseReady, MessageKeys.CaseDelivered, MessageKeys.CaseLink, MessageKeys.SurveyRequest], smartx.Keys);
        Assert.Equal([MessageKeys.AuthOtp], legacy.Keys);
    }

    private sealed class Recorder(string name) : ISmsProvider
    {
        public string Name => name;
        public List<string> Keys { get; } = [];
        public Task<SmsSendResult> SendTemplateAsync(string mobile, string messageKey, IReadOnlyDictionary<string, string> tokens, CancellationToken ct)
        {
            Keys.Add(messageKey);
            return Task.FromResult(new SmsSendResult(true, "id", null));
        }
    }

    // ───────── helpers ─────────

    private sealed class Disposable(Rig rig) : IDisposable
    {
        public Rig Inner => rig;
        public void Dispose() => rig.Scope.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static Disposable Wrap(Rig rig) => new(rig);

    private static string Sent(Rig rig) =>
        JsonDocument.Parse(rig.Http.Bodies[^1]).RootElement.GetProperty("recipientInfo")[0].GetProperty("body").GetString()!;

    private static string Status(JsonElement list, string key) =>
        list.GetProperty("templates").EnumerateArray().Single(t => t.GetProperty("key").GetString() == key).GetProperty("status").GetString()!;
}
