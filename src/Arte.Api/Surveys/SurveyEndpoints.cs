using System.Text.Json;
using Arte.Api.Cases;
using Arte.Api.Security;
using Arte.Core.Cases;
using Arte.Core.Common;
using Arte.Core.Data;
using Arte.Core.Identity;
using Arte.Core.Surveys;
using Arte.Core.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Arte.Api.Surveys;

public static class SurveyEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public sealed record AnswerRequest(List<SurveyService.Answer>? Answers, string? Comment);
    public sealed record FollowUp(string? Note);
    public sealed record ReadRequest(Guid[]? Ids);
    public sealed record QuestionInput(string? Text, Guid? TenantId, int? SortOrder, bool? IsActive);

    public static void MapSurveys(this IEndpointRouteBuilder app)
    {
        // ── the customer, from the tracking page (no sign-in; the tracking code is the key) ──
        app.MapPost("/api/v1/track/{code}/survey", async (string code, AnswerRequest req, IServiceScopeFactory scopes, CancellationToken ct) =>
        {
            if (!Arte.Api.Tracking.TrackingEndpoints.ValidCode(code)) return Results.NotFound();
            await using var scope = scopes.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            var db = sp.GetRequiredService<ArteDbContext>();
            var found = await db.Cases.IgnoreQueryFilters([ArteDbContext.TenantFilter]).AsNoTracking()
                .Where(c => c.TrackingCode == code && !c.IsSample).Select(c => new { c.Id, c.TenantId }).SingleOrDefaultAsync(ct);
            if (found is null) return Results.NotFound();
            sp.GetRequiredService<TenantContext>().Set(found.TenantId);
            var t = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == found.TenantId, ct);
            if (!t.IsActive || !t.SurveyEnabled) return Results.NotFound();

            var c = await db.Cases.AsNoTracking().SingleAsync(x => x.Id == found.Id, ct);
            var error = await sp.GetRequiredService<SurveyService>().AnswerAsync(c, req.Answers ?? [], req.Comment, ct);
            return error is null ? Results.NoContent() : Results.Problem(statusCode: 400, title: error);
        }).AllowAnonymous().RequireRateLimiting("session");

        // ── the business ──
        app.MapPost("/api/v1/cases/{id:guid}/survey/follow-up", async (Guid id, FollowUp req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var m = me.RequiredMembership;
            var c = await db.Cases.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
            if (c is null || !CaseAccess.CanSee(m, c)) return Results.NotFound();
            if (!CaseAccess.CanSeeAll(m)) return Results.Problem(statusCode: 403, title: "پیگیری نظر مشتری با مدیر است.");
            var invite = await db.SurveyInvites.SingleOrDefaultAsync(x => x.CaseId == id && x.AnsweredAt != null, ct);
            if (invite is null) return Results.NotFound();
            var note = req.Note?.Trim();
            if (note is { Length: > 300 })
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = ["یادداشت حداکثر ۳۰۰ حرف."] });
            if (invite.FollowedUpAt is not null) return Results.NoContent();

            var now = clock.UtcNow;
            invite.FollowedUpAt = now;
            invite.FollowedUpBy = me.RequiredUserId;
            invite.FollowUpNote = string.IsNullOrEmpty(note) ? null : note;
            db.CaseEvents.Add(new CaseEvent
            {
                TenantId = c.TenantId, CaseId = c.Id, Type = "survey.followed_up", ActorUserId = me.RequiredUserId, OccurredAt = now,
                Data = JsonSerializer.SerializeToDocument(new { Note = invite.FollowUpNote }, Json),
            });
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireTenant();

        app.MapGet("/api/v1/notifications", async (RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var mid = me.RequiredMembership.Id;
            var since = clock.UtcNow.AddDays(-90);
            var mine = from r in db.NotificationRecipients
                       join n in db.Notifications on r.NotificationId equals n.Id
                       where r.MembershipId == mid && n.CreatedAt >= since
                       select new { n, r.ReadAt };
            var items = await mine.OrderByDescending(x => x.n.CreatedAt).Take(50)
                .Select(x => new { x.n.Id, x.n.Type, x.n.CaseId, x.n.Title, x.n.Body, x.n.IsAlert, x.n.CreatedAt, Read = x.ReadAt != null })
                .ToListAsync(ct);
            return Results.Ok(new { Unread = await mine.CountAsync(x => x.ReadAt == null, ct), Items = items });
        }).RequireTenant().RequireRateLimiting("session");

        app.MapPost("/api/v1/notifications/read", async (ReadRequest req, RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            if (req.Ids is { Length: > 100 }) return Results.ValidationProblem(new Dictionary<string, string[]> { ["ids"] = ["حداکثر ۱۰۰ مورد."] });
            var mid = me.RequiredMembership.Id;
            var q = db.NotificationRecipients.Where(r => r.MembershipId == mid && r.ReadAt == null);
            if (req.Ids is { } ids) q = q.Where(r => ids.Contains(r.NotificationId));
            var now = clock.UtcNow;
            await q.ExecuteUpdateAsync(u => u.SetProperty(r => r.ReadAt, now), ct);
            return Results.NoContent();
        }).RequireTenant();

        // A member's own score: the cases they were responsible for (also for technicians without reports).
        app.MapGet("/api/v1/surveys/mine", async (RequestUser me, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var m = me.RequiredMembership;
            if (!await db.Tenants.AnyAsync(t => t.Id == m.TenantId && t.SurveyEnabled, ct)) return Results.Ok(new { Enabled = false });
            var since = clock.UtcNow.AddDays(-90);
            var answered = await db.SurveyInvites.AsNoTracking()
                .Where(i => i.AnsweredAt >= since && i.ResponsibleIds.Contains(m.Id)).ToListAsync(ct);
            return Results.Ok(new { Enabled = true, Days = 90, Responses = answered.Count, Average = Avg(answered), SatisfiedPercent = Satisfied(answered) });
        }).RequireTenant();

        // ── platform admin: the questions (general, or extra for one business) ──
        var admin = app.MapGroup("/api/v1/admin/survey-questions").RequirePlatformAdmin();
        admin.MapGet("/", async (Guid? tenantId, ArteDbContext db, CancellationToken ct) =>
            Results.Ok(await db.SurveyQuestions.AsNoTracking().Where(q => q.TenantId == tenantId)
                .OrderBy(q => q.SortOrder).ThenBy(q => q.CreatedAt)
                .Select(q => new { q.Id, q.TenantId, q.Text, q.SortOrder, q.IsActive }).ToListAsync(ct)));

        admin.MapPost("/", async (QuestionInput req, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var text = req.Text?.Trim();
            if (text is null || text.Length is < 5 or > 200)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["متن سؤال بین ۵ تا ۲۰۰ حرف."] });
            if (req.TenantId is { } tid && !await db.Tenants.AnyAsync(t => t.Id == tid, ct)) return Results.NotFound();
            if (await db.SurveyQuestions.CountAsync(q => q.IsActive && q.TenantId == req.TenantId, ct) >= SurveyRules.MaxActiveQuestions)
                return Results.Problem(statusCode: 409, title: $"حداکثر {SurveyRules.MaxActiveQuestions} سؤال فعال؛ اول یکی را غیرفعال کنید.");
            var now = clock.UtcNow;
            var order = req.SortOrder ?? (await db.SurveyQuestions.Where(q => q.TenantId == req.TenantId).MaxAsync(q => (int?)q.SortOrder, ct) ?? 0) + 1;
            var q = new SurveyQuestion { TenantId = req.TenantId, Text = text, SortOrder = order, CreatedAt = now, UpdatedAt = now };
            db.SurveyQuestions.Add(q);
            await db.SaveChangesAsync(ct);
            return Results.Created($"/api/v1/admin/survey-questions/{q.Id}", new { q.Id });
        });

        admin.MapPatch("/{id:guid}", async (Guid id, QuestionInput req, ArteDbContext db, IClock clock, CancellationToken ct) =>
        {
            var q = await db.SurveyQuestions.SingleOrDefaultAsync(x => x.Id == id, ct);
            if (q is null) return Results.NotFound();
            var text = req.Text?.Trim();
            if (text is not null && text.Length is < 5 or > 200)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["text"] = ["متن سؤال بین ۵ تا ۲۰۰ حرف."] });
            if (req.IsActive == true && !q.IsActive
                && await db.SurveyQuestions.CountAsync(x => x.IsActive && x.TenantId == q.TenantId, ct) >= SurveyRules.MaxActiveQuestions)
                return Results.Problem(statusCode: 409, title: $"حداکثر {SurveyRules.MaxActiveQuestions} سؤال فعال؛ اول یکی را غیرفعال کنید.");
            if (text is not null) q.Text = text;
            if (req.SortOrder is { } order) q.SortOrder = order;
            if (req.IsActive is { } active) q.IsActive = active;
            q.UpdatedAt = clock.UtcNow;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    // ───────── shared views ─────────

    public static decimal? Avg(IReadOnlyCollection<SurveyInvite> answered) =>
        answered.Count == 0 ? null : Math.Round(answered.Average(i => i.Score ?? 0), 2);

    /// <summary>CSAT: the share of answers averaging 4 or 5.</summary>
    public static int? Satisfied(IReadOnlyCollection<SurveyInvite> answered) =>
        answered.Count == 0 ? null : (int)Math.Round(100.0 * answered.Count(i => i.Score >= 4) / answered.Count);

    /// <summary>The survey part of a case page; null when the business has no survey for it.</summary>
    public static async Task<object?> CaseViewAsync(ArteDbContext db, Case c, CancellationToken ct)
    {
        var i = await db.SurveyInvites.AsNoTracking().SingleOrDefaultAsync(x => x.CaseId == c.Id, ct);
        if (i is null) return null;
        var answers = i.AnsweredAt is null ? [] : await db.SurveyAnswers.AsNoTracking().Where(a => a.InviteId == i.Id)
            .Join(db.SurveyQuestions, a => a.QuestionId, q => q.Id, (a, q) => new { a.QuestionText, a.Rating, q.TenantId, q.SortOrder })
            .OrderBy(x => x.TenantId != null).ThenBy(x => x.SortOrder)
            .Select(x => new { Question = x.QuestionText, x.Rating }).ToListAsync(ct);
        var ids = i.ResponsibleIds;
        var responsible = await db.Memberships.AsNoTracking().Where(m => ids.Contains(m.Id))
            .Select(m => m.User!.DisplayName ?? m.User.Mobile).ToListAsync(ct);
        var customer = await db.Customers.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]).AsNoTracking()
            .Where(x => x.Id == i.CustomerId).Select(x => x.FullName).SingleOrDefaultAsync(ct);
        var followedBy = i.FollowedUpBy is null ? null
            : await db.Users.Where(u => u.Id == i.FollowedUpBy).Select(u => u.DisplayName ?? u.Mobile).SingleOrDefaultAsync(ct);
        return new
        {
            State = i.AnsweredAt is not null ? "answered" : i.CancelledAt is not null ? "cancelled" : i.SentAt is not null ? "sent"
                : i.Attempts >= SurveyRules.MaxAttempts ? "failed" : "scheduled",
            i.DueAt, i.SentAt, i.AnsweredAt, i.ExpiresAt, i.Score, i.IsLow, i.Comment, CustomerName = customer,
            Answers = answers, Responsible = responsible,
            i.FollowedUpAt, FollowedUpBy = followedBy, i.FollowUpNote,
        };
    }

    /// <summary>For the reports page: response rate, CSAT, each question, each staff member, the latest low ones.</summary>
    public static async Task<object?> ReportAsync(ArteDbContext db, Guid tenantId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId && t.SurveyEnabled, ct)) return null;
        var sent = await db.SurveyInvites.CountAsync(i => i.SentAt >= from && i.SentAt < to, ct);
        var answered = await db.SurveyInvites.AsNoTracking().Where(i => i.AnsweredAt >= from && i.AnsweredAt < to).ToListAsync(ct);
        var inviteIds = answered.Select(i => i.Id).ToList();
        var answers = await db.SurveyAnswers.AsNoTracking().Where(a => inviteIds.Contains(a.InviteId))
            .Join(db.SurveyQuestions, a => a.QuestionId, q => q.Id, (a, q) => new { a.QuestionId, q.Text, q.TenantId, q.SortOrder, a.Rating })
            .ToListAsync(ct);
        var questions = answers.GroupBy(a => a.QuestionId)
            .Select(g => new { Question = g.First().Text, g.First().TenantId, g.First().SortOrder, Average = Math.Round(g.Average(a => a.Rating), 2), Responses = g.Count() })
            .OrderBy(x => x.TenantId != null).ThenBy(x => x.SortOrder)
            .Select(x => new { x.Question, x.Average, x.Responses }).ToList();

        var members = await db.Memberships.AsNoTracking().Include(m => m.User).OrderBy(m => m.CreatedAt).ToListAsync(ct);
        var staff = members.Select(m =>
        {
            var mine = answered.Where(i => i.ResponsibleIds.Contains(m.Id)).ToList();
            return new { MembershipId = m.Id, Name = m.User!.DisplayName ?? m.User.Mobile, Responses = mine.Count, Average = Avg(mine), SatisfiedPercent = Satisfied(mine) };
        }).Where(x => x.Responses > 0).OrderByDescending(x => x.Responses).ToList();

        var lowIds = answered.Where(i => i.IsLow).OrderByDescending(i => i.AnsweredAt).Take(10).Select(i => i.CaseId).ToList();
        var low = await (from i in db.SurveyInvites.AsNoTracking()
                         where lowIds.Contains(i.CaseId)
                         join c in db.Cases on i.CaseId equals c.Id
                         join cu in db.Customers.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]) on c.CustomerId equals cu.Id
                         where c.DeletedAt == null
                         orderby i.AnsweredAt descending
                         select new { c.Id, c.Number, Customer = cu.FullName, i.Score, i.Comment, i.AnsweredAt, Followed = i.FollowedUpAt != null })
            .ToListAsync(ct);

        return new
        {
            Sent = sent, Responses = answered.Count,
            ResponseRatePercent = sent == 0 ? (int?)null : (int)Math.Round(100.0 * Math.Min(answered.Count, sent) / sent),
            Average = Avg(answered), SatisfiedPercent = Satisfied(answered),
            Questions = questions, Staff = staff, Low = low,
        };
    }

    /// <summary>Low-satisfaction cases nobody has followed up yet (home screen, managers).</summary>
    public static async Task<List<LowCase>> NeedsFollowUpAsync(ArteDbContext db, CancellationToken ct) =>
        await (from i in db.SurveyInvites.AsNoTracking()
               where i.IsLow && i.AnsweredAt != null && i.FollowedUpAt == null
               join c in db.Cases on i.CaseId equals c.Id
               join cu in db.Customers.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]) on c.CustomerId equals cu.Id
               join a in db.Assets.IgnoreQueryFilters([ArteDbContext.SoftDeleteFilter]) on c.AssetId equals a.Id into aj
               from a in aj.DefaultIfEmpty()
               where c.DeletedAt == null
               orderby i.AnsweredAt descending
               select new LowCase(c.Id, c.Number, cu.FullName, a == null ? null : a.Title, i.Score!.Value, i.Comment, i.AnsweredAt!.Value))
            .Take(20).ToListAsync(ct);

    public sealed record LowCase(Guid Id, long Number, string? CustomerName, string? AssetTitle, decimal Score, string? Comment, DateTimeOffset AnsweredAt);
}
