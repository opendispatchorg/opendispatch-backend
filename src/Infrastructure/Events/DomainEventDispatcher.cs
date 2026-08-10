using System.Collections.Concurrent;
using MediatR;
using OpenDispatch.Application.Messaging;
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
    /// <summary>
    /// The closed <see cref="DomainEventNotification{TEvent}"/> for each event type met so far.
    /// </summary>
    /// <remarks>
    /// The event's concrete type is only known at run time, so the wrapper has to be closed
    /// reflectively. Only the type construction is cached, not a compiled factory: a handful of
    /// events per transaction does not earn an expression tree, and <c>Activator</c> over a
    /// cached type is a few hundred nanoseconds against a database round trip.
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, Type> NotificationTypes = new();

    /// <summary>Publishes everything queued, oldest first, and everything that raises.</summary>
    public async Task DispatchAsync(CancellationToken ct)
    {
        // Draining until empty rather than once, because a handler may itself change an
        // aggregate and save — raising events that would otherwise sit in the queue waiting for
        // a dispatch that never comes.
        for (var batch = queue.Drain(); batch.Count > 0; batch = queue.Drain())
        {
            foreach (var domainEvent in batch)
            {
                await publisher.Publish(Wrap(domainEvent), ct).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Throws away what is queued, for work that was not committed.</summary>
    public void Discard() => queue.Discard();

    private static INotification Wrap(IDomainEvent domainEvent)
    {
        var notificationType = NotificationTypes.GetOrAdd(
            domainEvent.GetType(),
            static eventType => typeof(DomainEventNotification<>).MakeGenericType(eventType));

        return (INotification)Activator.CreateInstance(notificationType, domainEvent)!;
    }
}
