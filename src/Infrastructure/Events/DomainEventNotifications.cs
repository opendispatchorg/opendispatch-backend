using System.Collections.Concurrent;
using MediatR;
using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Common;

namespace OpenDispatch.Infrastructure.Events;

/// <summary>
/// Puts a domain event into the shape the messaging library delivers.
/// </summary>
/// <remarks>
/// <para>
/// The event's concrete type is only known at run time — in the request that raised it and, since
/// the outbox, in a sweep that read it back out of a row — so the wrapper has to be closed
/// reflectively. Both publishers need it, which is why it is here rather than private to either.
/// </para>
/// <para>
/// Only the closed type is cached, not a compiled factory: a handful of events per transaction does
/// not earn an expression tree, and <c>Activator</c> over a cached type is a few hundred nanoseconds
/// against a database round trip.
/// </para>
/// </remarks>
internal static class DomainEventNotifications
{
    private static readonly ConcurrentDictionary<Type, Type> NotificationTypes = new();

    /// <summary>The event, as the notification its handlers subscribe to.</summary>
    public static INotification Wrap(IDomainEvent domainEvent)
    {
        var notificationType = NotificationTypes.GetOrAdd(
            domainEvent.GetType(),
            static eventType => typeof(DomainEventNotification<>).MakeGenericType(eventType));

        return (INotification)Activator.CreateInstance(notificationType, domainEvent)!;
    }
}
