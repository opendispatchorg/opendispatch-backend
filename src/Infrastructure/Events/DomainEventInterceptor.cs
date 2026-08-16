using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using OpenDispatch.Domain.Common;
using OpenDispatch.Infrastructure.Persistence;

namespace OpenDispatch.Infrastructure.Events;

/// <summary>
/// Takes the domain events off the aggregates a save is about to write, and writes them down with
/// it.
/// </summary>
/// <remarks>
/// <para>
/// Document 2 §6: events are dispatched from a save interceptor, which "keeps event publication
/// reliable and out of the handlers' hands". No handler remembers to collect them, so no handler
/// can forget — an aggregate that raised an event has announced it by virtue of being saved.
/// </para>
/// <para>
/// <strong>Collection moved from after the save to before it, and that is the outbox.</strong> The
/// events are turned into <see cref="OutboxMessage"/> rows and added to the same <c>SaveChanges</c>
/// as the aggregates that raised them, so the fact and the record of it are one write: either a job
/// is cancelled and something is going to hear about it, or neither happened. Collecting afterwards
/// could only ever put them somewhere a process could take with it when it died.
/// </para>
/// <para>
/// They are also queued in memory, because the outbox is the guarantee and not the mechanism: the
/// ordinary path still publishes in-process the moment the transaction commits, so a board is live
/// rather than waiting for a sweep. What the row buys is what happens when that publish never
/// runs.
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

    /// <summary>
    /// Turns the events raised in this transaction into rows of the same save.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cleared from the aggregates as they are taken, so a second save in the same transaction — a
    /// handler that saves partway through, then the pipeline saving again — cannot record the same
    /// event twice.
    /// </para>
    /// <para>
    /// Adding entities from inside this interceptor is deliberate and supported: it runs before EF
    /// turns the change tracker into commands, so the rows join the batch rather than needing a
    /// save of their own.
    /// </para>
    /// </remarks>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        var context = eventData.Context;

        if (context is not null)
        {
            foreach (var root in Raised(context))
            {
                foreach (var domainEvent in root.DomainEvents)
                {
                    context.Add(OutboxMessage.For(domainEvent, DomainEventSerializer.Serialize(domainEvent)));
                }

                queue.Enqueue(root.DomainEvents);
                root.ClearDomainEvents();
            }
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        // Nothing is collected here any more: SavingChangesAsync took the events and wrote them
        // down as part of this very save. What is left is the decision about when to publish.
        //
        // A save with no transaction around it *was* a transaction: EF opened one, committed it,
        // and only then called this. There is nothing left to wait for, so the events go out now.
        // When TransactionBehavior has opened one, the rows are written but not yet real, and
        // UnitOfWorkTransaction.CommitAsync is what makes them so — and what dispatches.
        if (eventData.Context is AppDbContext context && context.Database.CurrentTransaction is null)
        {
            var delivered = await dispatcher.DispatchAsync(cancellationToken).ConfigureAwait(false);

            await OutboxTrail.ForgetAsync(context, delivered, cancellationToken).ConfigureAwait(false);
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
