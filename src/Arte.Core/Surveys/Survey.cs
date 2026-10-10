using Arte.Core.Tenancy;

namespace Arte.Core.Surveys;

/// <summary>
/// A customer-satisfaction question. Only the platform admin writes these: TenantId null is asked of every business,
/// a TenantId adds the question for that one business. Never deleted (answers keep a copy of the text); switched off instead.
/// </summary>
public sealed class SurveyQuestion
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid? TenantId { get; set; }
    public required string Text { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// One survey per delivered case: scheduled at delivery, sent by SMS later (SurveySender), answered once from the
/// customer's tracking page. The people responsible are fixed at delivery so later reassignment does not move the score.
/// </summary>
public sealed class SurveyInvite : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid CaseId { get; set; }
    public Guid CustomerId { get; set; }
    /// <summary>Memberships: the assignee at delivery and whoever did the labor lines.</summary>
    public Guid[] ResponsibleIds { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>When the SMS should go (delivery + delay, moved into sending hours); also the next retry time.</summary>
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    /// <summary>Taken by a sender run; a stale claim (crash) is taken again after a few minutes.</summary>
    public DateTimeOffset? ClaimedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    /// <summary>Case reopened, deleted, or the survey was switched off before the SMS went.</summary>
    public DateTimeOffset? CancelledAt { get; set; }

    public DateTimeOffset? AnsweredAt { get; set; }
    /// <summary>Average of the star answers (1–5).</summary>
    public decimal? Score { get; set; }
    public bool IsLow { get; set; }
    public string? Comment { get; set; }

    public DateTimeOffset? FollowedUpAt { get; set; }
    public Guid? FollowedUpBy { get; set; }
    public string? FollowUpNote { get; set; }
}

public sealed class SurveyAnswer : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public Guid InviteId { get; set; }
    public Guid QuestionId { get; set; }
    /// <summary>The question as the customer saw it.</summary>
    public required string QuestionText { get; set; }
    public int Rating { get; set; }
}

public static class SurveyRules
{
    public const int MaxActiveQuestions = 5;
    public const int MinDelayMinutes = 5;
    public const int MaxDelayMinutes = 72 * 60;
    public const int ValidDays = 7;
    public const int MaxAttempts = 3;
    /// <summary>Survey SMS go out only between 09:00 and 23:00 Tehran time; other customer SMS are sent at once.</summary>
    public const int SendFromHour = 9, SendUntilHour = 23;
    private static readonly TimeSpan Tehran = TimeSpan.FromHours(3.5);   // Iran has no DST since 2022.

    /// <summary>Low satisfaction: any answer of 2 or less, or an average under 3.</summary>
    public static bool IsLow(IReadOnlyCollection<int> ratings) => ratings.Any(r => r <= 2) || ratings.Average() < 3;

    /// <summary>The same moment if it falls in sending hours, otherwise the next 09:00 Tehran time.</summary>
    public static DateTimeOffset IntoSendingHours(DateTimeOffset utc)
    {
        var local = utc.ToOffset(Tehran);
        if (local.Hour >= SendFromHour && local.Hour < SendUntilHour) return utc;
        var day = local.Hour < SendFromHour ? local.Date : local.Date.AddDays(1);
        return new DateTimeOffset(day.AddHours(SendFromHour), Tehran).ToUniversalTime();
    }
}

/// <summary>
/// A message in the bell. One row per event; who gets it and whether they read it is per person (NotificationRecipient).
/// Kept 90 days in the list. Built so other kinds (payment, parts arrived, trial ending) can be added later.
/// </summary>
public sealed class Notification : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid TenantId { get; set; }
    public required string Type { get; set; }
    public Guid? CaseId { get; set; }
    public required string Title { get; set; }
    public string? Body { get; set; }
    /// <summary>Shown with a red label (low satisfaction).</summary>
    public bool IsAlert { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class NotificationRecipient : ITenantOwned
{
    public Guid NotificationId { get; set; }
    public Guid TenantId { get; set; }
    /// <summary>Membership, so a person in two businesses has two inboxes.</summary>
    public Guid MembershipId { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
}

public static class NotificationTypes
{
    public const string SurveyAnswered = "survey.answered";
}
