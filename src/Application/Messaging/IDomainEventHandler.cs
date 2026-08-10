using System.Diagnostics.CodeAnalysis;
using MediatR;
using OpenDispatch.Domain.Common;

namespace OpenDispatch.Application.Messaging;

/// <summary>
/// Reacts to something the domain says happened. The extension seam of the whole system.
/// </summary>
/// <typeparam name="TEvent">The event to subscribe to.</typeparam>
/// <remarks>
/// <para>
/// This is the interface Document 2 §12 and <c>CLAUDE.md</c> promise: automatic invoicing, the
/// customer survey, inventory decrement and technician commission all arrive as new classes
/// implementing this, never as an edit to the code that completes a job. Adding one is the
/// whole ceremony — the registration is an assembly scan, and the dispatch is the persistence
/// layer's business.
/// </para>
/// <para>
/// A handler runs <em>after the transaction that raised the event has committed</em>, so what it
/// reacts to is a fact and not a proposal. It runs inside the same request, before the caller
/// gets its answer, and a handler that throws fails that request — see
/// <c>DomainEventDispatcher</c> for what that means and does not mean.
/// </para>
/// <para>
/// One class may implement this several times over to subscribe to several events.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// internal sealed class RaiseInvoiceOnCompletion(/* ports */) : IDomainEventHandler&lt;JobCompleted&gt;
/// {
///     public Task Handle(JobCompleted domainEvent, CancellationToken ct) => /* ... */;
/// }
/// </code>
/// </example>
[SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "The rule reserves the 'EventHandler' suffix for delegates in the .NET event pattern, which this is not — it is a subscriber to a domain event. The name is the one Document 2 section 12 and CLAUDE.md give this seam, and it is what a reader of either will look for.")]
public interface IDomainEventHandler<TEvent> : INotificationHandler<DomainEventNotification<TEvent>>
    where TEvent : IDomainEvent
{
    /// <summary>Reacts to the event.</summary>
    /// <param name="domainEvent">What happened.</param>
    /// <param name="cancellationToken">The request's token.</param>
    Task Handle(TEvent domainEvent, CancellationToken cancellationToken);

    /// <summary>
    /// Unwraps the notification, so an implementer writes a method about the domain rather than
    /// one about the messaging library. Nothing should override this.
    /// </summary>
    Task INotificationHandler<DomainEventNotification<TEvent>>.Handle(
        DomainEventNotification<TEvent> notification,
        CancellationToken cancellationToken) =>
        Handle(notification.DomainEvent, cancellationToken);
}
