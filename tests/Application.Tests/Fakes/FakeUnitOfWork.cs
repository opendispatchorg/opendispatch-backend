using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Tests.Fakes;

/// <summary>
/// A unit of work that writes to the stores instead of a database.
/// </summary>
/// <remarks>
/// It takes every store rather than one, because a save is one save: a handler that changed two
/// aggregates would otherwise have half its work written, which is the arrangement the real unit
/// of work exists to prevent.
/// </remarks>
internal sealed class FakeUnitOfWork(IEnumerable<IStagedWrites> stores) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        foreach (var store in stores)
        {
            store.Write();
        }

        return Task.FromResult(0);
    }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct) =>
        Task.FromResult<IUnitOfWorkTransaction>(new Transaction(stores));

    /// <remarks>
    /// One attempt, always: retrying is the real unit of work's business, and a fake that retried
    /// would be asserting a policy rather than standing in for one. What a retry does to the
    /// pipeline is proved by <c>RecordingUnitOfWork</c> in the pipeline tests.
    /// </remarks>
    public async Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<IUnitOfWorkTransaction, CancellationToken, Task<TResult>> work,
        CancellationToken ct)
    {
        await using var transaction = await BeginTransactionAsync(ct).ConfigureAwait(false);

        return await work(transaction, ct).ConfigureAwait(false);
    }

    private sealed class Transaction(IEnumerable<IStagedWrites> stores) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken ct)
        {
            foreach (var store in stores)
            {
                store.Discard();
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>A tenant context that acts as one organization for the life of a test.</summary>
internal sealed class FixedTenant(OrgId orgId) : ITenantContext
{
    public OrgId OrgId { get; } = orgId;
}

/// <summary>
/// A clock stopped at an instant a test can state, and move.
/// </summary>
/// <remarks>
/// The reason <c>IClock</c> is a port at all: a handler that stamps "now" onto a completed job is
/// only testable if the present is something the test decides.
/// </remarks>
internal sealed class FixedClock : IClock
{
    /// <summary>The instant this clock reports. Settable, for a test that needs time to pass.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);
}
