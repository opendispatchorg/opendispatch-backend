using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenDispatch.Domain.Common;

namespace OpenDispatch.Infrastructure.Events;

/// <summary>
/// Takes the domain events off the aggregates a save has just written.
/// </summary>
/// <remarks>
/// <para>
/// Document 2 §6: events are dispatched from a save interceptor, which "keeps event publication
/// reliable and out of the handlers' hands". No handler remembers to collect them, so no handler
/// can forget — an aggregate that raised an event has announced it by virtue of being saved.
/// </para>
/// <para>
/// Collection happens <em>after</em> a successful save, so a save that failed leaves the events
/// on their aggregates: nothing happened, and nothing is queued to say it did.
/// </para>
/// </remarks>
internal sealed class DomainEventInterceptor(DomainEventQueue queue, DomainEventDispatcher dispatcher)
    : SaveChangesInterceptor
{
    /// <summary>
    /// Refuses a synchronous save that would drop events.
    /// </summary>
    /// <remarks>
    /// Publishing is asynchronous, so only the async path can dispatch. Rather than let a
    /// synchronous save quietly succeed and take the events with it — the sort of loss nothing
    /// downstream can report, because the symptom is an invoice that was never raised — the save
    /// is refused before it happens. Every port in the application is async, so nothing in the
    /// system takes this path today.
    /// </remarks>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (Raised(eventData.Context).Count > 0)
        {
            throw new InvalidOperationException(
                "Saving synchronously would discard the domain events raised in this transaction, "
                    + "because publishing them is asynchronous. Save through IUnitOfWork.SaveChangesAsync.");
        }

        return base.SavingChanges(eventData, result);
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        foreach (var root in Raised(eventData.Context))
        {
            queue.Enqueue(root.DomainEvents);

            // Cleared as they are taken, so a second save in the same transaction — a handler
            // that saves partway through, then the pipeline saving again — cannot collect the
            // same event twice and announce it twice.
            root.ClearDomainEvents();
        }

        // A save with no transaction around it *was* a transaction: EF opened one, committed it,
        // and only then called this. There is nothing left to wait for, so the events go out now.
        // When TransactionBehavior has opened one, the rows are written but not yet real, and
        // UnitOfWorkTransaction.CommitAsync is what makes them so — and what dispatches.
        if (eventData.Context?.Database.CurrentTransaction is null)
        {
            await dispatcher.DispatchAsync(cancellationToken).ConfigureAwait(false);
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The tracked aggregates holding events, in the order the change tracker knows them.
    /// </summary>
    /// <remarks>
    /// Order within one aggregate is the order it raised them, which is the ordering that
    /// carries meaning — a job goes en route before it is in progress. Across aggregates it is
    /// tracking order, which is stable for a given handler but is not a promise the domain makes.
    /// </remarks>
    private static IReadOnlyList<AggregateRoot> Raised(DbContext? context) =>
        context is null
            ? []
            : [.. context.ChangeTracker
                .Entries<AggregateRoot>()
                .Select(entry => entry.Entity)
                .Where(root => root.DomainEvents.Count > 0)];
}
