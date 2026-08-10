using MediatR;
using OpenDispatch.Domain.Common;

namespace OpenDispatch.Application.Messaging;

/// <summary>
/// A domain event on its way to its handlers.
/// </summary>
/// <typeparam name="TEvent">The event that happened.</typeparam>
/// <param name="DomainEvent">What the aggregate said it did.</param>
/// <remarks>
/// <para>
/// The wrapper exists because <see cref="IDomainEvent"/> lives in the Domain, which depends on
/// nothing and so cannot implement <c>INotification</c>. Rather than let the messaging library
/// reach into the domain, the domain's fact is carried by a shape that belongs to the
/// application layer.
/// </para>
/// <para>
/// It is generic rather than a single <c>DomainEventNotification(IDomainEvent)</c> so a
/// subscriber names the one event it cares about. A non-generic wrapper would deliver every
/// event to every handler and leave each of them to switch on a type — which is the shape that
/// makes adding a feature an edit to existing code rather than a new file.
/// </para>
/// <para>
/// Handlers do not normally name this type: <see cref="IDomainEventHandler{TEvent}"/> unwraps
/// it, so a subscriber sees the event itself.
/// </para>
/// </remarks>
public sealed record DomainEventNotification<TEvent>(TEvent DomainEvent) : INotification
    where TEvent : IDomainEvent;
