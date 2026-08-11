using OpenDispatch.Application.Sync;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Issues the watermark a device syncs against.
/// </summary>
/// <remarks>
/// <para>
/// The contract is one sentence with two halves, and the halves are what make offline sync
/// survivable: a cursor taken now is at or below every change that is not yet committed, and it
/// never moves backwards. Everything a device has not been told about is therefore at or after
/// the cursor it holds — <em>at</em> included, which is why "changed since" is inclusive of the
/// cursor and a pull may repeat a change it has already sent.
/// </para>
/// <para>
/// Repeating is the correct side to fail on. A pull hands over whole entity states rather than
/// diffs, so seeing one twice costs a device nothing; missing one leaves a technician driving to
/// a job that was cancelled an hour ago.
/// </para>
/// <para>
/// <strong>Take the cursor before reading what changed, never after.</strong> A change committed
/// between the two reads is below a cursor taken second, and a device that stores that cursor
/// would never be told about it. Taken first, the same change is at or after the cursor and
/// arrives on the next pull.
/// </para>
/// </remarks>
public interface ISyncCursorSource
{
    /// <summary>
    /// The watermark as it stands: at or below anything still being written.
    /// </summary>
    Task<SyncCursor> CurrentAsync(CancellationToken ct);
}
