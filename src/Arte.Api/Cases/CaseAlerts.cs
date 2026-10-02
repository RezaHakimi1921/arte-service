using Arte.Core.Cases;
using Arte.Core.Workflows;

namespace Arte.Api.Cases;

/// <summary>
/// Plain-language reasons a case needs someone's attention. Shown on cards and in the home inbox,
/// so people know what to do without opening the case.
/// </summary>
public static class CaseAlerts
{
    public const int StuckAfterDays = 2;
    private static readonly TimeSpan Tehran = TimeSpan.FromHours(3.5); // Iran has no DST since 2022.

    public sealed record Alert(string Code, string Text, string Severity);

    public static DateOnly TehranDay(DateTimeOffset t) => DateOnly.FromDateTime(t.ToOffset(Tehran).DateTime);

    public static DateTimeOffset TehranMidnightUtc(DateTimeOffset now) =>
        new DateTimeOffset(now.ToOffset(Tehran).Date, Tehran).ToUniversalTime();

    public static List<Alert> All(string category, Guid? assigneeId, DateTimeOffset stageEnteredAt, DateTimeOffset? promisedAt,
        string? waitReason, DateTimeOffset now, bool forManager)
    {
        var list = new List<Alert>();
        var days = (int)Math.Floor((now - stageEnteredAt).TotalDays);
        var done = category == StageCategories.Done;

        if (promisedAt is { } p && !done)
        {
            var due = TehranDay(p);
            var today = TehranDay(now);
            if (due < today) list.Add(new("overdue", "قول تحویل گذشته", "danger"));
            else if (due == today) list.Add(new("due_today", "قول تحویل امروز", "warn"));
        }
        if (forManager && assigneeId is null && !done) list.Add(new("unassigned", "مسئول ندارد", "warn"));
        if (waitReason == WaitReasons.CustomerApproval && days >= 1) list.Add(new("approval_late", $"{days} روز منتظر تأیید مشتری", "warn"));
        if (waitReason == WaitReasons.OwnerDecision && forManager) list.Add(new("owner_decision", "منتظر تصمیم استاد", "warn"));
        if (waitReason is null && !done && days >= StuckAfterDays) list.Add(new("stuck", $"{days} روز در این مرحله", "warn"));
        if (waitReason is not null && waitReason != WaitReasons.CustomerApproval && days >= StuckAfterDays)
            list.Add(new("waiting_long", $"{days} روز متوقف", "warn"));
        return list;
    }

    public static Alert? Primary(bool terminal, string category, Guid? assigneeId, DateTimeOffset stageEnteredAt,
        DateTimeOffset? promisedAt, string? waitReason, DateTimeOffset now) =>
        terminal ? null : All(category, assigneeId, stageEnteredAt, promisedAt, waitReason, now, forManager: true).FirstOrDefault();
}
