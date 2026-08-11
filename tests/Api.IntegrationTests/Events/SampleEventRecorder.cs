using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Events;

namespace OpenDispatch.Api.IntegrationTests.Events;

/// <summary>
/// What the subscribers saw, and when.
/// </summary>
/// <remarks>
/// A singleton the test owns, so the handlers — which the container makes fresh each time —
/// all report to one place.
/// </remarks>
internal sealed class DomainEventRecorder
{
    private readonly List<IDomainEvent> _received = [];

    /// <summary>Every event delivered, in the order it was delivered.</summary>
    public IReadOnlyList<IDomainEvent> Received => _received;

    /// <summary>
    /// Run as each event arrives, before the handler returns.
    /// </summary>
    /// <remarks>
    /// This is how a test asks the interesting questions: what could an outside connection see
    /// at the moment this arrived, and what happens to the request if a subscriber throws.
    /// </remarks>
    public Func<IDomainEvent, CancellationToken, Task>? OnReceived { get; set; }

    /// <summary>Records an event and runs whatever the test wanted done when it arrived.</summary>
    public async Task RecordAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        _received.Add(domainEvent);

        if (OnReceived is not null)
        {
            await OnReceived(domainEvent, cancellationToken);
        }
    }
}

/// <summary>
/// Subscribes to the three events the sample command raises, and to the one the planning slices
/// raise.
/// </summary>
/// <remarks>
/// One class implementing <see cref="IDomainEventHandler{TEvent}"/> three times, which is both
/// the shape a real subscriber takes and a demonstration that one class may watch several
/// events. Nothing registers it by name: the container finds it by scanning, which is the whole
/// of what "extend by adding a handler" is supposed to cost.
/// </remarks>
internal sealed class SampleEventRecorder(DomainEventRecorder recorder)
    : IDomainEventHandler<JobEnRoute>,
        IDomainEventHandler<JobInProgress>,
        IDomainEventHandler<JobCompleted>,
        IDomainEventHandler<AssignmentChanged>,
        IDomainEventHandler<InvoicePaid>
{
    public Task Handle(AssignmentChanged domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(domainEvent, cancellationToken);

    public Task Handle(InvoicePaid domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(domainEvent, cancellationToken);

    public Task Handle(JobEnRoute domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(domainEvent, cancellationToken);

    public Task Handle(JobInProgress domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(domainEvent, cancellationToken);

    public Task Handle(JobCompleted domainEvent, CancellationToken cancellationToken) =>
        recorder.RecordAsync(domainEvent, cancellationToken);
}
