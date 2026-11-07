using System.Collections.Concurrent;

namespace Arte.Api.Sms.SmartX;

/// ذخیرهٔ UUID هر بچ «قبل از ارسال». اگر برنامه وسط کار بیفتد، بچ در حالت Pending می‌ماند
/// و با همان UUID دوباره فرستاده می‌شود (ResumePendingAsync).
public interface ISmsBatchLog
{
    /// قبل از اولین تماس شبکه‌ای صدا زده می‌شود. بچ ۵ دقیقه قفل است تا Resume همزمان آن را برندارد.
    Task BeginAsync(Guid batchId, IReadOnlyList<SmsRecipient> recipients, CancellationToken ct);

    Task UpdateAsync(Guid batchId, SmsBatchState state, int? httpStatus, string? error, CancellationToken ct);

    /// بچ‌های Pending بدون قفل را برمی‌دارد و ۵ دقیقه قفل می‌کند.
    Task<IReadOnlyList<PendingBatch>> ClaimPendingAsync(int max, CancellationToken ct);
}

/// پیش‌فرض: فقط در حافظه (با ری‌استارت برنامه از بین می‌رود). برای ذخیرهٔ پایدار در Postgres باید یک پیاده‌سازی EF جدا نوشت (فاز بعد).
public sealed class InMemorySmsBatchLog : ISmsBatchLog
{
    private static readonly TimeSpan LockFor = TimeSpan.FromMinutes(5);

    private sealed class Entry
    {
        public required IReadOnlyList<SmsRecipient> Recipients { get; init; }
        public SmsBatchState State;
        public DateTimeOffset? LockedUntil;
        public int? HttpStatus;
        public string? Error;
    }

    private readonly ConcurrentDictionary<Guid, Entry> _items = new();

    public Task BeginAsync(Guid batchId, IReadOnlyList<SmsRecipient> recipients, CancellationToken ct)
    {
        _items[batchId] = new Entry
        {
            Recipients = recipients,
            State = SmsBatchState.Pending,
            LockedUntil = DateTimeOffset.UtcNow + LockFor,
        };
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Guid batchId, SmsBatchState state, int? httpStatus, string? error, CancellationToken ct)
    {
        if (_items.TryGetValue(batchId, out var e))
            lock (e)
            {
                e.State = state;
                e.HttpStatus = httpStatus;
                e.Error = error;
                e.LockedUntil = null;
            }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PendingBatch>> ClaimPendingAsync(int max, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var result = new List<PendingBatch>();
        foreach (var (id, e) in _items)
        {
            if (result.Count >= max) break;
            lock (e)
            {
                if (e.State != SmsBatchState.Pending || (e.LockedUntil is { } l && l > now)) continue;
                e.LockedUntil = now + LockFor;
                result.Add(new PendingBatch(id, e.Recipients));
            }
        }
        return Task.FromResult<IReadOnlyList<PendingBatch>>(result);
    }

    /// برای تست و عیب‌یابی
    public (SmsBatchState State, int? HttpStatus, string? Error)? Get(Guid batchId) =>
        _items.TryGetValue(batchId, out var e) ? (e.State, e.HttpStatus, e.Error) : null;
}
