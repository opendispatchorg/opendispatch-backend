using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Sync;

namespace OpenDispatch.Infrastructure.Persistence;

/// <inheritdoc cref="ISyncCursorSource"/>
/// <remarks>
/// <para>
/// One question to Postgres, and the answer is the promise the port makes: the xmin of the
/// current snapshot is the oldest transaction that had not finished when the snapshot was taken,
/// so everything below it is settled and everything a device cannot see yet is at or above it.
/// </para>
/// <para>
/// It goes through the request's own <see cref="AppDbContext"/> rather than a connection of its
/// own, so the watermark and the changes read next to it come from the same connection — and, when
/// a transaction is open, the same transaction. A cursor taken on a different connection would
/// describe a different moment than the rows it is issued with.
/// </para>
/// </remarks>
internal sealed class SyncCursorSource(AppDbContext context) : ISyncCursorSource
{
    public async Task<SyncCursor> CurrentAsync(CancellationToken ct)
    {
        // Interpolated into the query rather than parameterised because it is a constant of this
        // assembly, not input: a parameter here would be a function call the server cannot make.
        var watermark = await context.Database
            .SqlQueryRaw<long>($"SELECT {ChangeStamps.WatermarkSql} AS \"Value\"")
            .SingleAsync(ct)
            .ConfigureAwait(false);

        return new SyncCursor(watermark);
    }
}
