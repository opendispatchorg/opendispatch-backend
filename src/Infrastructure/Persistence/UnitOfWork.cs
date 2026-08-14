using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Infrastructure.Events;

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
internal sealed class UnitOfWork(AppDbContext context, DomainEventDispatcher dispatcher) : IUnitOfWork
{
    /// <inheritdoc />
    /// <remarks>
    /// The one thing translated on the way out: EF's <c>DbUpdateConcurrencyException</c> becomes
    /// <see cref="ConcurrencyConflictException"/>, the port's own word for it. Every aggregate root
    /// carries a <c>Version</c> concurrency token (Document 2 §6), so this is what a second
    /// dispatcher's save looks like when the first one's landed while they were both looking at the
    /// same job — an ordinary refusal, and one <c>ConcurrencyBehavior</c> turns into a
    /// <c>Result</c>. Translated here because <c>Application</c> may not reference EF Core and so
    /// cannot catch that type by name.
    /// </remarks>
    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            return await context.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException lost)
        {
            throw new ConcurrencyConflictException(
                "Another change to this data was committed first.", lost);
        }
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct) =>
        new UnitOfWorkTransaction(
            await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false),
            dispatcher);
}
