using OpenDispatch.Application.Sync;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Reads everything that has changed in one technician's world since a cursor.
/// </summary>
/// <remarks>
/// <para>
/// A read port rather than a repository, for the reason <see cref="IDispatchBoardReadModel"/>
/// gives: a device wants a customer's name and a site's address against every job, and neither
/// lives on <c>Job</c>. Assembling that from aggregates would be a customer load per job on the
/// path that has to work over a phone connection.
/// </para>
/// <para>
/// <strong>The scope is the technician's, and it is defined by the plan.</strong> A job is in it
/// because a stop for it is theirs — so a job whose stop moved to somebody else leaves the scope
/// with the stop, and a job with no stop at all was never in it. That is the only definition that
/// needs no per-device state on the server, which is what keeps sync retriable.
/// </para>
/// <para>
/// <strong>Since is inclusive.</strong> The cursor is the oldest transaction that could still have
/// been in flight when it was issued, so a change stamped exactly at it may or may not have been
/// seen. Sending it again is free — every change here is a whole state, so applying one twice
/// writes the same thing twice — while missing one leaves a technician driving to a cancelled job.
/// </para>
/// <para>
/// It reports removals over-generously for the same reason: a stop that changed and is no longer
/// this technician's is named whether or not the device ever held it, because the server does not
/// know what the device holds and a removal for something absent is a no-op.
/// </para>
/// </remarks>
public interface ISyncChangeReader
{
    /// <summary>
    /// One page of everything in this technician's world stamped at or after
    /// <paramref name="since"/>.
    /// </summary>
    /// <param name="technician">Whose world.</param>
    /// <param name="since">
    /// The cursor the device last stored. <c>SyncCursor.Beginning</c> means it has never synced, and
    /// gets everything from the beginning — a page at a time.
    /// </param>
    /// <param name="maxTransactions">
    /// How many <em>transactions</em> a page may carry. Not a row count: the rows one transaction
    /// wrote share a stamp and cannot be split across pages without a device that resumes forever
    /// at the same cursor, so the reader stops between stamps and may overrun a row budget to do
    /// it. See <see cref="SyncScopePage"/>.
    /// </param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// The caller must take the <em>new</em> cursor before calling this, never after: a change
    /// committed between the two would be below a cursor taken second and would never be sent. That
    /// cursor is only the answer when the page turns out to be the whole of what was waiting —
    /// otherwise the caller resumes from <see cref="SyncScopePage.Ceiling"/>.
    /// </remarks>
    Task<SyncScopePage> ReadAsync(
        TechnicianId technician,
        SyncCursor since,
        int maxTransactions,
        CancellationToken ct);
}
