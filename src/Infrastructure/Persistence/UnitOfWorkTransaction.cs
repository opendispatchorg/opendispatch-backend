using Microsoft.EntityFrameworkCore.Storage;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Infrastructure.Events;

namespace OpenDispatch.Infrastructure.Persistence;

/// <inheritdoc cref="IUnitOfWorkTransaction"/>
/// <remarks>
/// A pass-through to EF's own transaction, which is what makes rollback real: the changes have
/// been written to the database by the time a handler decides the request failed, and only the
/// database can take them back. Disposing an uncommitted transaction rolls it back, so the
/// <c>await using</c> in the behavior is the guarantee rather than the tidy-up.
/// </remarks>
internal sealed class UnitOfWorkTransaction(
    IDbContextTransaction transaction,
    DomainEventDispatcher dispatcher)
    : IUnitOfWorkTransaction
{
    public async Task CommitAsync(CancellationToken ct)
    {
        await transaction.CommitAsync(ct).ConfigureAwait(false);

        // Only now is what happened a fact, so only now may anything be told about it. Publishing
        // on the save instead would push a board event, or email a customer, for a job whose
        // completion the next line could still roll back.
        await dispatcher.DispatchAsync(ct).ConfigureAwait(false);
    }

    public Task RollbackAsync(CancellationToken ct) => transaction.RollbackAsync(ct);

    public async ValueTask DisposeAsync()
    {
        // Anything still queued belongs to work that was not committed — a rolled-back command, a
        // thrown handler, a cancelled request. A commit has already emptied the queue, so this is
        // a no-op on the path that succeeded and the whole point on every path that did not.
        dispatcher.Discard();

        await transaction.DisposeAsync().ConfigureAwait(false);
    }
}
