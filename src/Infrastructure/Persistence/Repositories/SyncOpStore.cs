using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Sync;

namespace OpenDispatch.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ISyncOpStore"/>
internal sealed class SyncOpStore(AppDbContext context) : ISyncOpStore
{
    /// <remarks>
    /// One round trip whatever the size of the batch, and only the ids come back — the log rows
    /// themselves say nothing a push needs, since an op that was applied is an op there is
    /// nothing left to do about.
    /// </remarks>
    public async Task<IReadOnlySet<SyncOpId>> FindAppliedAsync(
        IReadOnlyCollection<SyncOpId> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return new HashSet<SyncOpId>();
        }

        var applied = await context.SyncOps
            .Where(op => ids.Contains(op.Id))
            .Select(op => op.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return applied.ToHashSet();
    }

    public void Add(SyncOpRecord op) => context.SyncOps.Add(op);
}
