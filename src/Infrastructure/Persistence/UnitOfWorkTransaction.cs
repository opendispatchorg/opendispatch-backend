using Microsoft.EntityFrameworkCore.Storage;
using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Infrastructure.Persistence;

/// <inheritdoc cref="IUnitOfWorkTransaction"/>
/// <remarks>
/// A pass-through to EF's own transaction, which is what makes rollback real: the changes have
/// been written to the database by the time a handler decides the request failed, and only the
/// database can take them back. Disposing an uncommitted transaction rolls it back, so the
/// <c>await using</c> in the behavior is the guarantee rather than the tidy-up.
/// </remarks>
internal sealed class UnitOfWorkTransaction(IDbContextTransaction transaction) : IUnitOfWorkTransaction
{
    public Task CommitAsync(CancellationToken ct) => transaction.CommitAsync(ct);

    public Task RollbackAsync(CancellationToken ct) => transaction.RollbackAsync(ct);

    public ValueTask DisposeAsync() => transaction.DisposeAsync();
}
