using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Infrastructure.Persistence;

namespace OpenDispatch.Infrastructure.Provisioning;

/// <summary>What a prune deleted.</summary>
/// <param name="Operations">Op-log rows removed.</param>
/// <param name="Removals">Removal notes removed.</param>
/// <param name="Before">The instant everything older than was deleted.</param>
public sealed record PrunedSyncLog(int Operations, int Removals, DateTimeOffset Before);

/// <summary>
/// Deletes the parts of the sync protocol's bookkeeping that have outlived their purpose.
/// </summary>
/// <remarks>
/// <para>
/// Two tables grow forever and neither is business data. The op log exists so a re-sent operation
/// is recognised rather than applied twice — which matters for as long as a device might still
/// re-send it, and no longer. The removal notes exist so a pull can report a stop that was deleted
/// — which matters for as long as a device might still be catching up.
/// </para>
/// <para>
/// <strong>What pruning costs, stated rather than discovered:</strong> a device whose cursor is
/// older than the retention window will not be told about stops deleted before it. That is not a
/// silent hole — a device that far behind is doing a full resync anyway, and the whole state it
/// receives is the truth — but it is the reason the window is configuration rather than a constant,
/// and the reason a shop with technicians who go months between syncs should raise it rather than
/// lower it.
/// </para>
/// <para>
/// The op log is the safer of the two: past the window, the worst a lost record can do is let a
/// device re-apply an operation whose effect is idempotent by construction (a status change to a
/// status the job already has is refused as an illegal transition; a note loses to the newer one
/// already recorded).
/// </para>
/// <para>
/// A verb rather than a hosted service, and that is a deployment decision as much as a design one:
/// a background sweep runs in every replica, so three hosts would run it three times. A cron job
/// runs it once and can be watched.
/// </para>
/// </remarks>
public sealed class SyncLogPruner(AppDbContext database, IClock clock)
{
    /// <summary>Deletes op-log rows and removal notes older than <paramref name="keepFor"/>.</summary>
    /// <param name="keepFor">How much history to keep.</param>
    /// <param name="ct">Cancels the work.</param>
    /// <remarks>
    /// Set-based deletes across every tenant, ignoring the query filters: this runs outside a
    /// request, so no tenant is resolved, and pruning is a housekeeping act on the whole database
    /// rather than something one organization asks for.
    /// </remarks>
    public async Task<PrunedSyncLog> PruneAsync(TimeSpan keepFor, CancellationToken ct)
    {
        var before = clock.UtcNow - keepFor;

        // Applied rows are stamped with when this server applied them, not with what the device
        // claimed — a phone with a broken clock cannot age its own operations out of the log early
        // or keep them in it forever.
        var operations = await database.SyncOps
            .IgnoreQueryFilters()
            .Where(op => op.AppliedAt < before)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        var removals = await database.SyncRemovals
            .IgnoreQueryFilters()
            .Where(removal => removal.RemovedAt < before)
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);

        return new PrunedSyncLog(operations, removals, before);
    }
}
