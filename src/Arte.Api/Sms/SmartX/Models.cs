namespace Arte.Api.Sms.SmartX;

/// یک گیرنده. Body متن نهایی و آماده است (شخصی‌سازی قبل از فراخوانی انجام می‌شود).
/// CorrelationId اختیاری و برای ردیابی هر پیام است.
public sealed record SmsRecipient(long RecipientId, string Mobile, string Body, string? CorrelationId = null);

public enum SmsBatchState { Pending, Accepted, Failed }

/// نتیجهٔ یک بچ (حداکثر ۱۰۰ گیرنده). Accepted یعنی اسمارت درخواست را پذیرفت (نه رسیدن به گوشی).
public sealed record SendBatchResult(
    Guid BatchId, int RecipientCount, SmsBatchState State, int? HttpStatus, string? Error, int Attempts)
{
    public bool Accepted => State == SmsBatchState.Accepted;
}

public sealed record SendResult(IReadOnlyList<SendBatchResult> Batches, IReadOnlyList<SmsRecipient> Invalid)
{
    public bool AllAccepted => Batches.All(b => b.Accepted);
    public int AcceptedRecipients => Batches.Where(b => b.Accepted).Sum(b => b.RecipientCount);
}

public sealed record PendingBatch(Guid BatchId, IReadOnlyList<SmsRecipient> Recipients);
