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
