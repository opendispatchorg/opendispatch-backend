using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Infrastructure.Persistence;

/// <inheritdoc cref="IUnitOfWork"/>
/// <remarks>
/// <para>
/// Two lines, because EF's <c>DbContext</c> already is a unit of work: the repositories stage
/// changes on the same scoped context, this commits them, and its <c>Database</c> is where a
/// transaction spanning several of those commits comes from. Wrapping any of it in more would
/// be re-implementing what is already there.
/// </para>
/// <para>
/// Atomicity over a single save comes from <c>SaveChangesAsync</c> itself, which opens a
/// transaction around the whole batch unless one is already open — and when
/// <c>TransactionBehavior</c> has opened one, that is exactly what happens: the save enlists
/// rather than committing on its own.
/// </para>
/// </remarks>
internal sealed class UnitOfWork(AppDbContext context) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct) => context.SaveChangesAsync(ct);

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct) =>
        new UnitOfWorkTransaction(await context.Database.BeginTransactionAsync(ct));
}
