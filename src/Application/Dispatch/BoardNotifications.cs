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
/// <strong>Two known gaps, both left exactly as the domain already draws them, not widened for
/// this step:</strong> a job reaching <see cref="JobStatus.Invoiced"/> raises nothing
/// (<c>Job.MarkInvoiced</c>'s own remarks explain why), and a brand-new stop raises nothing
/// (<c>Assignment.Create</c>'s own remarks, since step 8). Both are pre-existing, deliberate
/// silences this step did not introduce and does not reverse; see <c>DECISIONS.local.md</c>.
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
