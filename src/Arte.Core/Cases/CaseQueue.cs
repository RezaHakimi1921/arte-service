namespace Arte.Core.Cases;

/// <summary>
/// Shared meanings for home stats and case-list filters (doc 33):
/// open = non-terminal; in progress = diagnosing / repairing / review only.
/// </summary>
public static class CaseQueue
{
    public static readonly string[] InProgressKeys = ["diagnosing", "repairing", "review"];

    public static bool IsInProgress(string stageKey, string? waitReason) =>
        waitReason is null && InProgressKeys.Contains(stageKey);

    public static bool IsWaiting(string stageCategory, string? waitReason) =>
        waitReason is not null || stageCategory == Workflows.StageCategories.Waiting;

    public static bool IsReady(string stageCategory, bool isTerminal) =>
        stageCategory == Workflows.StageCategories.Done && !isTerminal;
}
