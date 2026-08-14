using Microsoft.EntityFrameworkCore;
using Npgsql;
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

        // The other half of the same race, caught by a different lock. Two writers *creating* the
        // row a unique index guards — two dispatchers planning one job, two re-plans of one day —
        // never read a version to be stale about, so the version token cannot see them and the
        // index is what refuses the second. Untranslated it is a DbUpdateException nothing catches:
        // an error-level log and a 500 for the most ordinary collision a dispatch board has.
        catch (DbUpdateException duplicated) when (IsDuplicate(duplicated))
        {
            throw new DuplicateRecordException(
                "Something else has already been written where this could only be written once.",
                duplicated);
        }
    }

    /// <summary>
    /// Whether the save failed because a unique index refused it.
    /// </summary>
    /// <remarks>
    /// Narrow on purpose: <c>DbUpdateException</c> also covers a foreign key, a check constraint
    /// and a not-null column, none of which is a race between two callers and all of which are
    /// bugs worth a 500 and a stack trace. Only the provider knows which is which, and it says so
    /// in <c>SqlState</c> — <c>23505</c>, the SQL standard's own code for a unique violation.
    /// </remarks>
    private static bool IsDuplicate(DbUpdateException failed) =>
        failed.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct) =>
        new UnitOfWorkTransaction(
            await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false),
            dispatcher);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// EF Core's execution strategy is what decides a failure is worth retrying, and it refuses to
    /// wrap a transaction somebody else opened — for a good reason: it cannot retry work whose
    /// boundary it does not own. So the boundary moves here, and the transaction is opened
    /// <em>inside</em> the strategy's own attempt.
    /// </para>
    /// <para>
    /// Every attempt is a fresh transaction and a fresh set of queued domain events: the
    /// transaction's dispose discards anything the failed attempt raised, so the retry cannot
    /// announce a fact from a run that was rolled back.
    /// </para>
    /// <para>
    /// What it does not do is reset the change tracker between attempts. Nothing in this system
    /// needs it to — a handler loads what it works on, and a retry re-runs the handler — but a
    /// caller that staged changes before calling this would be handing the second attempt the
    /// first one's leftovers.
    /// </para>
    /// </remarks>
    public Task<TResult> ExecuteInTransactionAsync<TResult>(
        Func<IUnitOfWorkTransaction, CancellationToken, Task<TResult>> work,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(work);

        return context.Database.CreateExecutionStrategy().ExecuteAsync(
            work,
            async (_, operation, token) =>
            {
                await using var transaction = await BeginTransactionAsync(token).ConfigureAwait(false);

                return await operation(transaction, token).ConfigureAwait(false);
            },
            verifySucceeded: null,
            cancellationToken: ct);
    }
}
