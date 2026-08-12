using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Sync.PullChanges;

/// <summary>
/// Takes the watermark, then reads everything at or after the one the device arrived with.
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
        var cursor = await cursors.CurrentAsync(cancellationToken).ConfigureAwait(false);

        var scope = await changes
            .ReadAsync(query.TechnicianId, query.Since, cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new PulledChanges(scope, cursor));
    }
}
