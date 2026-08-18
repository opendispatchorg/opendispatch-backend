using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Application.Dispatch;

/// <summary>
/// Turns the domain events the board already cares about into pushes over
/// <see cref="IBoardNotifier"/> (Document 2 §9, step 51).
/// </summary>
/// <remarks>
/// <para>
/// One class subscribing several times over, exactly as <c>IDomainEventHandler{TEvent}</c>'s own
/// remarks invite: every job-lifecycle event that already exists gets the same treatment, because
/// the board's whole interest in a job is its current status, not which transition produced it.
/// </para>
/// <para>
/// <strong>It re-reads the aggregate rather than trusting the event's own fields.</strong> A
/// domain event carries only what the aggregate said at the moment it acted — none of the seven
/// job events carry a version, and none needed to until now — so the handler asks the repository
/// for the current row instead. It runs after the transaction that raised the event has committed
/// (<c>IDomainEventHandler</c>'s own guarantee), so the read is of a fact, not a proposal, and it
/// costs one query per event rather than a version threaded through eight event shapes for a
/// reader that did not exist when they were written.
/// </para>
/// <para>
/// <strong>A newly planned stop and a moved one are drawn the same way.</strong>
/// <c>AssignmentPlanned</c> and <c>AssignmentChanged</c> both become <c>assignment.updated</c>,
/// because what a board wants is "this stop is now thus", not "this stop is new" — it has no
/// separate way to render an arrival, and a client that had to tell them apart would be a client
/// that could get it wrong. The two stay separate events for the subscribers that <em>do</em> need
/// the difference; see <c>AssignmentPlanned</c>'s own remarks.
/// </para>
/// <para>
/// <strong>One known gap remains, exactly as the domain draws it:</strong> a job reaching
/// <see cref="JobStatus.Invoiced"/> raises nothing (<c>Job.MarkInvoiced</c>'s own remarks explain
/// why). It is pre-existing and deliberate. The other gap this class used to name — a brand-new
/// stop raising nothing — was not deliberate in effect, whatever it was in intent: it meant
/// optimising a day repainted no board at all, and it is closed.
/// </para>
/// </remarks>
internal sealed class BoardNotifications(IJobRepository jobs, IAssignmentRepository assignments, IBoardNotifier notifier)
    : IDomainEventHandler<JobScheduled>,
      IDomainEventHandler<JobUnscheduled>,
      IDomainEventHandler<JobDispatched>,
      IDomainEventHandler<JobEnRoute>,
      IDomainEventHandler<JobInProgress>,
      IDomainEventHandler<JobCompleted>,
      IDomainEventHandler<JobCancelled>,
      IDomainEventHandler<AssignmentPlanned>,
      IDomainEventHandler<AssignmentChanged>,
      IDomainEventHandler<InvoicePaid>
{
    public Task Handle(JobScheduled domainEvent, CancellationToken cancellationToken) =>
        PublishJobAsync(domainEvent.JobId, cancellationToken);

    public Task Handle(JobUnscheduled domainEvent, CancellationToken cancellationToken) =>
        PublishJobAsync(domainEvent.JobId, cancellationToken);

    public Task Handle(JobDispatched domainEvent, CancellationToken cancellationToken) =>
        PublishJobAsync(domainEvent.JobId, cancellationToken);

    public Task Handle(JobEnRoute domainEvent, CancellationToken cancellationToken) =>
        PublishJobAsync(domainEvent.JobId, cancellationToken);

    public Task Handle(JobInProgress domainEvent, CancellationToken cancellationToken) =>
        PublishJobAsync(domainEvent.JobId, cancellationToken);

    public Task Handle(JobCompleted domainEvent, CancellationToken cancellationToken) =>
        PublishJobAsync(domainEvent.JobId, cancellationToken);

    public Task Handle(JobCancelled domainEvent, CancellationToken cancellationToken) =>
        PublishJobAsync(domainEvent.JobId, cancellationToken);

    /// <remarks>
    /// The one case the job that changed is not the job that raised the event: settling an
    /// invoice is <c>Invoice</c>'s own act, and <c>InvoicePaid</c> is what carries the job's
    /// identity to anything that wants to react — the same seam <c>CLAUDE.md</c>/Document 2 §12
    /// promise, used here rather than invented for this step.
    /// </remarks>
    public Task Handle(InvoicePaid domainEvent, CancellationToken cancellationToken) =>
        PublishJobAsync(domainEvent.JobId, cancellationToken);

    /// <remarks>
    /// The case that had never worked. Both slices that plan work create stops through
    /// <c>Assignment.Create</c>, and until it announced itself an optimised day reached every board
    /// as silence — the dispatcher who pressed the button saw the new plan on their next read, and
    /// nobody else saw it at all.
    /// </remarks>
    public Task Handle(AssignmentPlanned domainEvent, CancellationToken cancellationToken) =>
        PublishAssignmentAsync(domainEvent.AssignmentId, cancellationToken);

    public Task Handle(AssignmentChanged domainEvent, CancellationToken cancellationToken) =>
        PublishAssignmentAsync(domainEvent.AssignmentId, cancellationToken);

    private async Task PublishJobAsync(JobId jobId, CancellationToken cancellationToken)
    {
        if (await jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false) is { } job)
        {
            await notifier.JobUpdatedAsync(job.Id, job.Status, job.Version, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task PublishAssignmentAsync(AssignmentId assignmentId, CancellationToken cancellationToken)
    {
        if (await assignments.GetAsync(assignmentId, cancellationToken).ConfigureAwait(false) is { } assignment)
        {
            await notifier.AssignmentUpdatedAsync(
                assignment.Id,
                assignment.JobId,
                assignment.TechnicianId,
                assignment.Sequence,
                assignment.ScheduledStart,
                assignment.TravelMin,
                assignment.Version,
                cancellationToken).ConfigureAwait(false);
        }
    }
}
