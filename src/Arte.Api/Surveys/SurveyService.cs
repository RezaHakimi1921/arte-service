using System.Text.Json;
using Arte.Api.Cases;
using Arte.Core.Cases;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Surveys;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Surveys;

/// <summary>
/// The satisfaction survey of a case: scheduled when it is delivered (if the business has the add-on and sends it),
/// cancelled when it is reopened or deleted before the SMS went, answered once by the customer.
/// Runs inside the case's business (tenant-filtered context).
/// </summary>
public sealed class SurveyService(ArteDbContext db, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Called in the delivery transaction (before SaveChanges). Delivering again after a reopen schedules it again.</summary>
    public async Task ScheduleOnDeliveryAsync(Case c, CancellationToken ct)
    {
        if (c.IsSample) return;
        var t = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == c.TenantId, ct);
        if (!t.SurveyEnabled || !t.SurveySendOn) return;

        var now = clock.UtcNow;
        var labor = await db.CaseItems.AsNoTracking()
            .Where(i => i.CaseId == c.Id && i.PerformedBy != null)
            .Select(i => i.PerformedBy!.Value).ToListAsync(ct);
        var responsible = (c.AssigneeId is { } a ? labor.Prepend(a) : labor).Distinct().ToArray();
        var due = SurveyRules.IntoSendingHours(now.AddMinutes(t.SurveyDelayMinutes));

        var invite = await db.SurveyInvites.SingleOrDefaultAsync(x => x.CaseId == c.Id, ct);
        if (invite is { AnsweredAt: not null }) return;      // already answered: keep it
        if (invite is null)
        {
            invite = new SurveyInvite { TenantId = c.TenantId, CaseId = c.Id, CreatedAt = now };
            db.SurveyInvites.Add(invite);
        }
        invite.CustomerId = c.CustomerId;
        invite.ResponsibleIds = responsible;
        invite.DueAt = due;
        invite.ExpiresAt = due.AddDays(SurveyRules.ValidDays);
        invite.CancelledAt = null;
        invite.ClaimedAt = null;
        invite.SentAt = null;
        invite.Attempts = 0;
        invite.LastError = null;
    }

    /// <summary>Reopened or deleted after delivery: an SMS that has not gone yet is not sent.</summary>
    public async Task CancelUnsentAsync(Guid caseId, CancellationToken ct)
    {
        var invite = await db.SurveyInvites.SingleOrDefaultAsync(x => x.CaseId == caseId, ct);
        if (invite is { SentAt: null, AnsweredAt: null, CancelledAt: null }) invite.CancelledAt = clock.UtcNow;
    }

    /// <summary>The questions this business asks now: the general ones, then its own, each in order.</summary>
    public static Task<List<SurveyQuestion>> QuestionsAsync(ArteDbContext db, Guid tenantId, CancellationToken ct) =>
        db.SurveyQuestions.AsNoTracking()
            .Where(q => q.IsActive && (q.TenantId == null || q.TenantId == tenantId))
            .OrderBy(q => q.TenantId != null).ThenBy(q => q.SortOrder).ThenBy(q => q.CreatedAt)
            .ToListAsync(ct);

    /// <summary>Whether the customer may answer now; null when they may, otherwise the state shown instead.</summary>
    public static string State(SurveyInvite i, DateTimeOffset now) =>
        i.AnsweredAt is not null ? "answered"
        : i.CancelledAt is not null ? "closed"
        : now > i.ExpiresAt ? "expired"
        : "open";

    public sealed record Answer(Guid QuestionId, int Rating);

    /// <summary>
    /// Saves the customer's answer once and tells the people who follow surveys. Returns a Persian error, or null.
    /// </summary>
    public async Task<string?> AnswerAsync(Case c, IReadOnlyList<Answer> answers, string? comment, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var invite = await db.SurveyInvites.SingleOrDefaultAsync(x => x.CaseId == c.Id, ct);
        if (invite is null) return "نظرسنجی برای این پرونده فعال نیست.";
        switch (State(invite, now))
        {
            case "answered": return "نظر شما قبلاً ثبت شده است. سپاسگزاریم.";
            case "closed": return "نظرسنجی برای این پرونده فعال نیست.";
            case "expired": return "زمان این نظرسنجی تمام شده است.";
        }

        var questions = await QuestionsAsync(db, c.TenantId, ct);
        if (questions.Count == 0) return "نظرسنجی برای این پرونده فعال نیست.";
        var given = answers.GroupBy(a => a.QuestionId).ToDictionary(g => g.Key, g => g.First().Rating);
        if (questions.Any(q => !given.TryGetValue(q.Id, out var r) || r is < 1 or > 5) || given.Keys.Any(k => questions.All(q => q.Id != k)))
            return "لطفاً به همه‌ی سؤال‌ها با ۱ تا ۵ ستاره جواب دهید.";
        comment = comment?.Trim();
        if (comment is { Length: > 500 }) return "متن نظر حداکثر ۵۰۰ حرف.";

        var ratings = questions.Select(q => given[q.Id]).ToList();
        foreach (var q in questions)
            db.SurveyAnswers.Add(new SurveyAnswer { TenantId = c.TenantId, InviteId = invite.Id, QuestionId = q.Id, QuestionText = q.Text, Rating = given[q.Id] });
        invite.AnsweredAt = now;
        invite.Score = Math.Round((decimal)ratings.Average(), 2);
        invite.IsLow = SurveyRules.IsLow(ratings);
        invite.Comment = string.IsNullOrEmpty(comment) ? null : comment;

        db.CaseEvents.Add(new CaseEvent
        {
            TenantId = c.TenantId, CaseId = c.Id, Type = "survey.answered", OccurredAt = now,
            Data = JsonSerializer.SerializeToDocument(new { invite.Score, invite.IsLow }, Json),
        });
        await NotifyAsync(c, invite, now, ct);
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>
    /// One bell notification for the answer, to every active member who follows surveys: all of them if they see
    /// every case, otherwise only for their own work (the people fixed on the invite).
    /// </summary>
    private async Task NotifyAsync(Case c, SurveyInvite invite, DateTimeOffset now, CancellationToken ct)
    {
        var members = await db.Memberships.AsNoTracking().Where(m => m.IsActive && m.SurveyNotify).ToListAsync(ct);
        var to = members.Where(m => CaseAccess.CanSeeAll(m) || invite.ResponsibleIds.Contains(m.Id)).Select(m => m.Id).ToList();
        if (to.Count == 0) return;

        var customer = await db.Customers.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
            .Where(x => x.Id == c.CustomerId).Select(x => x.FullName).SingleAsync(ct);
        var vehicle = c.AssetId is null ? null : await db.Assets.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
            .Where(x => x.Id == c.AssetId).Select(x => x.Title).SingleOrDefaultAsync(ct);
        var who = string.IsNullOrWhiteSpace(customer) ? "مشتری" : customer.Trim();
        var n = new Notification
        {
            TenantId = c.TenantId, Type = NotificationTypes.SurveyAnswered, CaseId = c.Id, CreatedAt = now, IsAlert = invite.IsLow,
            Title = Cut(vehicle is null ? $"{who} نظر داد" : $"{who} برای {vehicle} نظر داد", 200),
            // No «·» next to Persian digits: it reads like a zero.
            Body = Cut($"امتیاز {Fa(invite.Score!.Value)} از ۵" + (invite.Comment is { } cm ? $"؛ «{cm}»" : ""), 300),
        };
        db.Notifications.Add(n);
        foreach (var m in to) db.NotificationRecipients.Add(new NotificationRecipient { NotificationId = n.Id, TenantId = c.TenantId, MembershipId = m });
    }

    private static string Cut(string s, int max) => s.Length <= max ? s : s[..(max - 1)].TrimEnd() + "…";

    /// <summary>4.33 → «۴٫۳»: one decimal, Persian digits.</summary>
    public static string Fa(decimal v)
    {
        var s = Math.Round(v, 1).ToString(v % 1 == 0 ? "0" : "0.0", System.Globalization.CultureInfo.InvariantCulture);
        return string.Concat(s.Select(ch => ch == '.' ? '٫' : char.IsAsciiDigit(ch) ? (char)('۰' + (ch - '0')) : ch));
    }
}
