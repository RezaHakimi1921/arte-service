namespace Arte.Core.Common;

/// <summary>
/// Records are never hard-deleted from the app: deleting sets DeletedAt and hides the row
/// through the soft_delete query filter; restoring clears it.
/// </summary>
public interface ISoftDeletable
{
    DateTimeOffset? DeletedAt { get; set; }
    Guid? DeletedBy { get; set; }
}
