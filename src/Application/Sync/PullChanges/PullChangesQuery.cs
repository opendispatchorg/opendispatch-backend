using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Sync.PullChanges;

/// <summary>
/// Everything a technician's device has missed, and where it now stands.
/// </summary>
/// <param name="TechnicianId">Whose world to read. Supplied from the authenticated principal at the edge.</param>
/// <param name="Since">
/// The cursor the device stored last time. <see cref="SyncCursor.Beginning"/> for a device that has
/// never synced, which gets everything in its scope.
/// </param>
/// <remarks>
/// <para>
/// A query rather than a command: it changes nothing, so it gets no transaction. What it does need
/// is an order — the new cursor is taken before anything is read — and that is the handler's one
/// piece of work.
/// </para>
/// <para>
/// Re-sending the same cursor is safe and returns the same window, which is what makes a response
/// lost to a dropped connection cost a repeat rather than a gap. A device only moves forward by
/// storing the cursor it was given.
/// </para>
/// </remarks>
/// <param name="MaxTransactions">
/// How much of the stream one pull may carry, counted in transactions rather than rows (see
/// <c>SyncScopePage</c>). Supplied by the edge from configuration rather than by the caller: a
/// device does not get to ask for the whole database, and an operator with a slow fleet gets a
/// lever.
/// </param>
public sealed record PullChangesQuery(TechnicianId TechnicianId, SyncCursor Since, int MaxTransactions)
    : IQuery<PulledChanges>;

/// <summary>
/// What the server holds for this technician, and the watermark to ask with next time.
/// </summary>
/// <param name="Changes">The work, the plan, and the stops that are gone.</param>
/// <param name="Cursor">
/// Where the device now stands. When this page is everything that was waiting, the watermark taken
/// before the read — so anything committed while the read was running is at or after it and arrives
/// next time rather than being skipped. When the page stopped early, one past the last transaction
/// it carried whole.
/// </param>
/// <param name="HasMore">
/// Whether the server stopped early. The device should pull again immediately with
/// <paramref name="Cursor"/> rather than waiting for its next scheduled sync.
/// </param>
public sealed record PulledChanges(SyncScopeChanges Changes, SyncCursor Cursor, bool HasMore);
