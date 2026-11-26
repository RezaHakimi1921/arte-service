namespace Arte.Core.Cases;

/// <summary>
/// Shared meanings for home stats and case-list filters (doc 33):
/// open = non-terminal; in progress = diagnosing / repairing / review (waitReason ignored);
/// waiting = stage category waiting only (not waitReason alone).
/// </summary>
public static class CaseQueue
{
    public static readonly string[] InProgressKeys = ["diagnosing", "repairing", "review"];

    public static bool IsInProgress(string stageKey) =>
        InProgressKeys.Contains(stageKey);

    public static bool IsWaiting(string stageCategory) =>
        stageCategory == Workflows.StageCategories.Waiting;

    public static bool IsReady(string stageCategory, bool isTerminal) =>
        stageCategory == Workflows.StageCategories.Done && !isTerminal;
}
