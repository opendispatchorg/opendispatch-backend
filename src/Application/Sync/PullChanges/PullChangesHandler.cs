using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Sync.PullChanges;

/// <summary>
/// Takes the watermark, then reads a page of everything at or after the one the device arrived
/// with.
/// </summary>
/// <remarks>
/// <para>
/// Two lines, and the order of them is the whole handler. <strong>The new cursor is taken before
/// the read.</strong> Taken after, a change committed while the read was running would sit below
/// the cursor the device stores — and a change below your cursor is never sent again, so a stop
/// cancelled at that moment would be one the technician drives to. Taken first, the same change is
/// at or after the cursor and arrives on the next pull.
/// </para>
/// <para>
/// The cost of that order is duplicates at the boundary, which is the trade the whole mechanism is
/// built on: every change is a whole state, so applying one twice writes the same thing twice.
/// </para>
/// <para>
/// <strong>The watermark is only the right answer when the page was everything.</strong> A device
/// that has never synced can have more waiting than belongs in one response over a phone
/// connection, so the read stops after a budget of transactions and says where it stopped — and
/// answering with the watermark then would tell the device it is up to date while the rest of its
/// day is still on the server.
/// </para>
/// <para>
/// Everything else is the read port's. What "this technician's scope" means, how a removal is
/// found, and which joins answer it are questions about the database, and the answer to all three
/// is one round of queries rather than a walk through aggregates.
/// </para>
/// </remarks>
internal sealed class PullChangesHandler(ISyncCursorSource cursors, ISyncChangeReader changes)
    : IRequestHandler<PullChangesQuery, Result<PulledChanges>>
{
    public async Task<Result<PulledChanges>> Handle(
        PullChangesQuery query,
        CancellationToken cancellationToken)
    {
        var watermark = await cursors.CurrentAsync(cancellationToken).ConfigureAwait(false);

        var page = await changes
            .ReadAsync(query.TechnicianId, query.Since, query.MaxTransactions, cancellationToken)
            .ConfigureAwait(false);

        // Two different answers to "where do I stand", and picking the wrong one is how a device
        // loses changes: the watermark means "up to date", so it may only be given when the page
        // carried everything. A page that stopped early answers with one past the last whole
        // transaction in it — every stamp at or below that has been sent, and a committed
        // transaction cannot gain rows afterwards.
        var cursor = page.Ceiling is { } ceiling ? new SyncCursor(ceiling + 1) : watermark;

        return Result.Success(new PulledChanges(page.Changes, cursor, page.Ceiling is not null));
    }
}
