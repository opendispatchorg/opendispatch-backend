using MediatR;
using OpenDispatch.Domain.Common;

namespace OpenDispatch.Infrastructure.Events;

/// <summary>
/// Publishes the queued domain events to whatever has subscribed to them.
/// </summary>
/// <remarks>
/// <para>
/// Called from exactly two places, and both mean the same thing: the work is committed. A
/// transaction's <c>CommitAsync</c> calls it, and so does the interceptor for a save that had no
/// transaction around it — because such a save was its own transaction and has already
/// committed by the time the interceptor runs.
/// </para>
/// <para>
/// <strong>Failures surface.</strong> Events are published one at a time and nothing is caught:
/// a handler that throws stops the rest and fails the request. That is the honest report — but
/// note what it does not mean, because the difference matters: the work is already committed, so
/// the request failing does not undo it. A handler with side effects the caller must not lose
/// belongs on a durable outbox, which is not in this system and is the right conversation to
/// have when one is needed.
/// </para>
/// <para>
/// MediatR arrives here transitively through Application, where the version is pinned and the
/// licensing reason for the pin is recorded.
/// </para>
/// </remarks>
internal sealed class DomainEventDispatcher(DomainEventQueue queue, IPublisher publisher)
{
    /// <summary>Publishes everything queued, oldest first, and everything that raises.</summary>
    /// <remarks>
    /// <para>
    /// It returns what it delivered so the caller can forget the outbox rows for those events —
    /// and the caller does that rather than this, because this must not know about the database.
    /// Taking an <c>AppDbContext</c> here is a dependency cycle (the context builds the interceptor
    /// that builds this), and one that recurses at construction rather than failing cleanly: the
    /// host simply stops, with no output, which cost an hour of bisecting to find.
    /// </para>
    /// <para>
    /// A failure part-way leaves the remaining rows, which is the whole point: the reaction is
    /// retried by <c>OutboxDispatcher</c> rather than lost with the request.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<IDomainEvent>> DispatchAsync(CancellationToken ct)
    {
        // Draining until empty rather than once, because a handler may itself change an
        // aggregate and save — raising events that would otherwise sit in the queue waiting for
        // a dispatch that never comes.
        var delivered = new List<IDomainEvent>();

        for (var batch = queue.Drain(); batch.Count > 0; batch = queue.Drain())
        {
            foreach (var domainEvent in batch)
            {
                await publisher.Publish(DomainEventNotifications.Wrap(domainEvent), ct).ConfigureAwait(false);

                delivered.Add(domainEvent);
            }
        }

        return delivered;
    }

    /// <summary>Throws away what is queued, for work that was not committed.</summary>
    /// <remarks>
    /// The outbox rows go with it, and they go by themselves: they were written by the same
    /// transaction as the work, so a rollback takes them too. Nothing has to remember to clean up
    /// after a command that failed.
    /// </remarks>
    public void Discard() => queue.Discard();


}
