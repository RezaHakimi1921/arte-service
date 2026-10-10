using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arte.Api.Surveys;
using Arte.Core.Data;
using Arte.Core.Surveys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Arte.Tests;

/// <summary>Customer satisfaction survey: scheduling at delivery, the SMS sender, answering, notifications, reports, admin questions.</summary>
[Collection(ApiCollection.Name)]
public sealed class SurveyTests(ArteApiFactory api)
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
        var db = scope.ServiceProvider.GetRequiredService<ArteDbContext>();
        await db.Users.Where(u => u.Mobile == mobile).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsPlatformAdmin, true));
        return client;
    }

    private async Task<(HttpClient Owner, Guid TenantId, HttpClient Admin)> BusinessWithSurveyAsync()
    {
        var (owner, tenantId) = await api.NewBusinessAsync();
        var admin = await AdminAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/v1/admin/businesses/{tenantId}", new { surveyEnabled = true })).StatusCode);
        return (owner, tenantId, admin);
    }

    private static async Task<(Guid Id, string Code, string Mobile)> OpenCase(HttpClient owner, Guid? assigneeId = null)
    {
        var mobile = ArteApiFactory.NewMobile();
        var id = (await Json(await owner.PostAsJsonAsync("/api/v1/cases", new
        {
            mobile, customerName = "سیما کریمی", reportedProblems = new[] { "روشن نمی‌شود" }, assigneeId,
            newAsset = new { title = "هوندا CG 125", kind = "motorcycle" },
        }))).GetProperty("id").GetGuid();
        var code = (await Json(await owner.GetAsync($"/api/v1/cases/{id}"))).GetProperty("trackingCode").GetString()!;
        return (id, code, mobile);
    }

    /// <summary>Runs the main button until the case is delivered (with a balance, delivered as credit).</summary>
    private static async Task<JsonElement> Deliver(HttpClient owner, Guid id)
    {
        var c = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        while (c.GetProperty("stage").GetProperty("key").GetString() != "delivered")
        {
            var primary = c.GetProperty("transitions").EnumerateArray().First(t => t.GetProperty("isPrimary").GetBoolean()).GetProperty("id");
            c = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{primary}", new { allowCredit = true }));
        }
        return c;
    }

    private Task<int> RunSender(DateTimeOffset at) => api.Services.GetRequiredService<SurveySender>().RunOnceAsync(CancellationToken.None, at);

    /// <summary>A moment after the invite is due, inside sending hours.</summary>
    private static DateTimeOffset AfterDue(JsonElement survey) =>
        SurveyRules.IntoSendingHours(survey.GetProperty("dueAt").GetDateTimeOffset().AddMinutes(1));

    private static object Answers(JsonElement track, params int[] ratings) =>
        track.GetProperty("survey").GetProperty("questions").EnumerateArray()
            .Select((q, i) => new { questionId = q.GetProperty("id").GetGuid(), rating = ratings[i] }).ToArray();

    [Fact]
    public async Task Delivery_schedules_the_survey_the_sender_sends_it_once_and_the_customer_answers_once()
    {
        var (owner, _, _) = await BusinessWithSurveyAsync();
        var techMobile = ArteApiFactory.NewMobile();
        var techId = (await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, displayName = "حسین", role = "technician" })))
            .GetProperty("id").GetGuid();
        var (id, code, mobile) = await OpenCase(owner, techId);
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/items", new { kind = "labor", title = "اجرت تعمیر", unitPriceRials = 1_000_000 }));

        var before = DateTimeOffset.UtcNow;
        var c = await Deliver(owner, id);
        var survey = c.GetProperty("survey");
        Assert.Equal("scheduled", survey.GetProperty("state").GetString());
        // 30 minutes after delivery by default, moved into sending hours.
        var due = survey.GetProperty("dueAt").GetDateTimeOffset();
        Assert.Equal(SurveyRules.IntoSendingHours(before.AddMinutes(30)), due, TimeSpan.FromMinutes(1));
        // Who did the work: the assignee and the person who recorded the labor (the owner).
        Assert.Equal(2, survey.GetProperty("responsible").GetArrayLength());

        // Nothing before it is due; once due, exactly one SMS with the tracking code (the pattern has no vehicle).
        await RunSender(due.AddMinutes(-5));
        Assert.False(api.Sms.Keys.TryGetValue(mobile, out var none) && none.Contains(MessageKeysSurvey));
        await RunSender(AfterDue(survey));
        await RunSender(AfterDue(survey).AddMinutes(1));
        Assert.Single(api.Sms.Keys[mobile], k => k == MessageKeysSurvey);
        var tokens = api.Sms.Sent[mobile];
        Assert.Equal(code, tokens["code"]);
        Assert.Equal("سیما کریمی", tokens["name"]);
        Assert.Equal("تعمیرگاه تست", tokens["shop"]);
        Assert.False(tokens.ContainsKey("vehicle"));

        // The tracking page carries the survey; every question is needed.
        var customer = api.Client();
        var track = await Json(await customer.GetAsync($"/api/v1/track/{code}"));
        Assert.Equal("open", track.GetProperty("survey").GetProperty("state").GetString());
        Assert.Equal(3, track.GetProperty("survey").GetProperty("questions").GetArrayLength());
        var partial = new { answers = new[] { new { questionId = track.GetProperty("survey").GetProperty("questions")[0].GetProperty("id").GetGuid(), rating = 5 } } };
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync($"/api/v1/track/{code}/survey", partial)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await customer.PostAsJsonAsync($"/api/v1/track/{code}/survey", new { answers = Answers(track, 5, 6, 5) })).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent,
            (await customer.PostAsJsonAsync($"/api/v1/track/{code}/survey", new { answers = Answers(track, 2, 4, 5), comment = "دیر تحویل شد" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await customer.PostAsJsonAsync($"/api/v1/track/{code}/survey", new { answers = Answers(track, 5, 5, 5) })).StatusCode);
        track = await Json(await customer.GetAsync($"/api/v1/track/{code}"));
        Assert.Equal("answered", track.GetProperty("survey").GetProperty("state").GetString());

        // On the case: the customer's name, each answer with its question, low satisfaction.
        survey = (await Json(await owner.GetAsync($"/api/v1/cases/{id}"))).GetProperty("survey");
        Assert.Equal("answered", survey.GetProperty("state").GetString());
        Assert.Equal("سیما کریمی", survey.GetProperty("customerName").GetString());
        Assert.True(survey.GetProperty("isLow").GetBoolean());
        Assert.Equal(3.67m, survey.GetProperty("score").GetDecimal());
        Assert.Equal("دیر تحویل شد", survey.GetProperty("comment").GetString());
        Assert.Equal(2, survey.GetProperty("answers")[0].GetProperty("rating").GetInt32());

        // The owner's bell has it (as an alert); the technician does not follow surveys by default.
        var bell = await Json(await owner.GetAsync("/api/v1/notifications"));
        Assert.Equal(1, bell.GetProperty("unread").GetInt32());
        var n = bell.GetProperty("items")[0];
        Assert.Equal(id, n.GetProperty("caseId").GetGuid());
        Assert.True(n.GetProperty("isAlert").GetBoolean());
        Assert.Contains("سیما کریمی", n.GetProperty("title").GetString());
        var (tech, _) = await api.LoginAsync(techMobile);
        Assert.Equal(0, (await Json(await tech.GetAsync("/api/v1/notifications"))).GetProperty("unread").GetInt32());

        // The technician sees their own score; the report counts it for both responsible people.
        var mine = await Json(await tech.GetAsync("/api/v1/surveys/mine"));
        Assert.Equal(1, mine.GetProperty("responses").GetInt32());
        var report = await Json(await owner.GetAsync($"/api/v1/reports/summary?from={Uri.EscapeDataString(before.AddDays(-1).ToString("O"))}&to={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("O"))}"));
        var rs = report.GetProperty("survey");
        Assert.Equal(1, rs.GetProperty("responses").GetInt32());
        Assert.Equal(0, rs.GetProperty("satisfiedPercent").GetInt32());
        Assert.Equal(3, rs.GetProperty("questions").GetArrayLength());
        Assert.Equal(2, rs.GetProperty("staff").GetArrayLength());
        Assert.Single(rs.GetProperty("low").EnumerateArray());

        // Low satisfaction stays on the home screen until a manager follows it up.
        var inbox = await Json(await owner.GetAsync("/api/v1/inbox"));
        Assert.Contains(inbox.GetProperty("lowSatisfaction").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.NotFound, (await tech.PostAsJsonAsync($"/api/v1/cases/{Guid.NewGuid()}/survey/follow-up", new { })).StatusCode);
        await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/survey/follow-up", new { note = "تماس گرفتم، قرار شد دوباره بیاید" }));
        inbox = await Json(await owner.GetAsync("/api/v1/inbox"));
        Assert.DoesNotContain(inbox.GetProperty("lowSatisfaction").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);

        await Json(await owner.PostAsJsonAsync("/api/v1/notifications/read", new { }));
        Assert.Equal(0, (await Json(await owner.GetAsync("/api/v1/notifications"))).GetProperty("unread").GetInt32());
    }

    private const string MessageKeysSurvey = Arte.Core.Messaging.MessageKeys.SurveyRequest;

    [Fact]
    public async Task Without_the_add_on_there_is_no_survey()
    {
        var (owner, _) = await api.NewBusinessAsync();
        var (id, code, _) = await OpenCase(owner);
        var c = await Deliver(owner, id);
        Assert.Equal(JsonValueKind.Null, c.GetProperty("survey").ValueKind);
        var track = await Json(await api.Client().GetAsync($"/api/v1/track/{code}"));
        Assert.Equal(JsonValueKind.Null, track.GetProperty("survey").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await api.Client().PostAsJsonAsync($"/api/v1/track/{code}/survey", new { answers = Array.Empty<object>() })).StatusCode);
        // The business cannot switch the add-on on itself.
        await Json(await owner.PutAsJsonAsync("/api/v1/settings/business", new { surveyEnabled = true, surveySendOn = true }));
        Assert.False((await Json(await owner.GetAsync("/api/v1/settings/business"))).GetProperty("surveyEnabled").GetBoolean());
    }

    [Fact]
    public async Task Reopening_before_the_sms_cancels_it_and_the_delay_setting_is_used()
    {
        var (owner, _, _) = await BusinessWithSurveyAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PutAsJsonAsync("/api/v1/settings/business", new { surveyDelayMinutes = 1 })).StatusCode);
        await Json(await owner.PutAsJsonAsync("/api/v1/settings/business", new { surveyDelayMinutes = 180 }));

        var (id, _, mobile) = await OpenCase(owner);
        var before = DateTimeOffset.UtcNow;
        var survey = (await Deliver(owner, id)).GetProperty("survey");
        Assert.Equal(SurveyRules.IntoSendingHours(before.AddMinutes(180)), survey.GetProperty("dueAt").GetDateTimeOffset(), TimeSpan.FromMinutes(1));

        var c = await Json(await owner.GetAsync($"/api/v1/cases/{id}"));
        var reopen = c.GetProperty("transitions").EnumerateArray().First(t => t.GetProperty("toStage").GetProperty("category").GetString() != "done").GetProperty("id");
        c = await Json(await owner.PostAsJsonAsync($"/api/v1/cases/{id}/transitions/{reopen}", new { reason = "صدای موتور برگشت" }));
        Assert.Equal("cancelled", c.GetProperty("survey").GetProperty("state").GetString());
        await RunSender(AfterDue(survey));
        Assert.False(api.Sms.Keys.TryGetValue(mobile, out var keys) && keys.Contains(MessageKeysSurvey));

        // Delivered again: scheduled again.
        Assert.Equal("scheduled", (await Deliver(owner, id)).GetProperty("survey").GetProperty("state").GetString());
    }

    [Fact]
    public async Task A_technician_who_follows_surveys_hears_only_about_their_own_work()
    {
        var (owner, _, _) = await BusinessWithSurveyAsync();
        var techMobile = ArteApiFactory.NewMobile();
        var techId = (await Json(await owner.PostAsJsonAsync("/api/v1/staff", new { mobile = techMobile, role = "technician" }))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.NoContent, (await owner.PatchAsJsonAsync($"/api/v1/staff/{techId}", new { surveyNotify = true })).StatusCode);
        var staff = await Json(await owner.GetAsync("/api/v1/staff"));
        Assert.True(staff.EnumerateArray().Single(s => s.GetProperty("id").GetGuid() == techId).GetProperty("surveyNotify").GetBoolean());

        var (theirs, theirCode, _) = await OpenCase(owner, techId);
        var (other, otherCode, _) = await OpenCase(owner);
        await Deliver(owner, theirs);
        await Deliver(owner, other);
        foreach (var code in new[] { theirCode, otherCode })
        {
            var track = await Json(await api.Client().GetAsync($"/api/v1/track/{code}"));
            await Json(await api.Client().PostAsJsonAsync($"/api/v1/track/{code}/survey", new { answers = Answers(track, 5, 5, 4) }));
        }

        var (tech, _) = await api.LoginAsync(techMobile);
        var bell = await Json(await tech.GetAsync("/api/v1/notifications"));
        Assert.Equal(theirs, bell.GetProperty("items").EnumerateArray().Single().GetProperty("caseId").GetGuid());
        Assert.False(bell.GetProperty("items")[0].GetProperty("isAlert").GetBoolean());
        Assert.Equal(2, (await Json(await owner.GetAsync("/api/v1/notifications"))).GetProperty("unread").GetInt32());
        // A technician cannot mark a follow-up.
        Assert.Equal(HttpStatusCode.Forbidden, (await tech.PostAsJsonAsync($"/api/v1/cases/{theirs}/survey/follow-up", new { })).StatusCode);
        // Another business sees nothing of it.
        var (stranger, _) = await api.NewBusinessAsync();
        Assert.Equal(0, (await Json(await stranger.GetAsync("/api/v1/notifications"))).GetProperty("unread").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsJsonAsync($"/api/v1/cases/{theirs}/survey/follow-up", new { })).StatusCode);
    }

    [Fact]
    public async Task Only_the_platform_admin_writes_questions_and_can_add_one_for_a_single_business()
    {
        var (owner, tenantId, admin) = await BusinessWithSurveyAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/v1/admin/survey-questions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync("/api/v1/admin/survey-questions", new { text = "سؤال من چیست؟", tenantId })).StatusCode);

        var general = await Json(await admin.GetAsync("/api/v1/admin/survey-questions"));
        Assert.True(general.GetArrayLength() >= 3);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/v1/admin/survey-questions", new { text = "کم", tenantId })).StatusCode);
        var qid = (await Json(await admin.PostAsJsonAsync("/api/v1/admin/survey-questions", new { text = "از تمیزی وسیله هنگام تحویل راضی بودید؟", tenantId })))
            .GetProperty("id").GetGuid();
        Assert.Single((await Json(await admin.GetAsync($"/api/v1/admin/survey-questions?tenantId={tenantId}"))).EnumerateArray());

        var (id, code, _) = await OpenCase(owner);
        await Deliver(owner, id);
        var track = await Json(await api.Client().GetAsync($"/api/v1/track/{code}"));
        var questions = track.GetProperty("survey").GetProperty("questions");
        Assert.Equal(general.EnumerateArray().Count(q => q.GetProperty("isActive").GetBoolean()) + 1, questions.GetArrayLength());
        Assert.Equal(qid, questions[questions.GetArrayLength() - 1].GetProperty("id").GetGuid());   // the business's own come last

        // Another business with the add-on does not get it.
        var (other, otherTenant) = await api.NewBusinessAsync();
        await Json(await admin.PatchAsJsonAsync($"/api/v1/admin/businesses/{otherTenant}", new { surveyEnabled = true }));
        var (oid, ocode, _) = await OpenCase(other);
        await Deliver(other, oid);
        var otherQs = (await Json(await api.Client().GetAsync($"/api/v1/track/{ocode}"))).GetProperty("survey").GetProperty("questions");
        Assert.DoesNotContain(otherQs.EnumerateArray(), q => q.GetProperty("id").GetGuid() == qid);

        // Switched off, not deleted.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/v1/admin/survey-questions/{qid}", new { isActive = false })).StatusCode);
        Assert.Single((await Json(await admin.GetAsync($"/api/v1/admin/survey-questions?tenantId={tenantId}"))).EnumerateArray());
    }

    [Theory]
    [InlineData("2026-10-10T08:30:00Z", "2026-10-10T08:30:00Z")]   // 12:00 Tehran: now
    [InlineData("2026-10-10T19:45:00Z", "2026-10-11T05:30:00Z")]   // 23:15 Tehran: next morning 09:00
    [InlineData("2026-10-10T02:00:00Z", "2026-10-10T05:30:00Z")]   // 05:30 Tehran: this morning 09:00
    [InlineData("2026-10-10T19:29:00Z", "2026-10-10T19:29:00Z")]   // 22:59 Tehran: still in hours
    public void Survey_sms_waits_for_sending_hours(string at, string expected) =>
        Assert.Equal(DateTimeOffset.Parse(expected), SurveyRules.IntoSendingHours(DateTimeOffset.Parse(at)));

    [Fact]
    public void Low_satisfaction_is_any_answer_of_two_or_an_average_under_three()
    {
        Assert.True(SurveyRules.IsLow([2, 5, 5]));
        Assert.True(SurveyRules.IsLow([3, 3, 2]));
        Assert.False(SurveyRules.IsLow([3, 3, 3]));
        Assert.False(SurveyRules.IsLow([4, 5, 3]));
    }
}
