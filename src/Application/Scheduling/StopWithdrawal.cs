using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Events;

namespace OpenDispatch.Application.Scheduling;

/// <summary>
/// Takes a called-off job's stop back out of the plan.
/// </summary>
/// <remarks>
/// <para>
/// A job can be cancelled from every status that has a stop — planned, on a phone, being driven to
/// — and cancelling it did not touch the plan: the row stayed on the technician's day. What that
/// leaves behind is exactly what <c>IAssignmentRepository.Remove</c>'s own remarks call the worst
/// outcome in this system, a stop somebody is expected to drive to for work that is not happening.
/// It also quietly costs the day capacity, because an emergency insertion plans around every stop
/// it can see, cancelled or not.
/// </para>
/// <para>
/// It arrives as a subscriber rather than as three lines inside <c>ChangeJobStatusHandler</c>,
/// which is the whole of Document 2 §12: the code that cancels a job is not opened, and the rule
/// holds for every other way a job will ever be cancelled — a sync op from a phone, a future bulk
/// action — without any of them remembering it. Removing the stop writes the note a technician's
/// pull reads (<c>SyncRemoval</c>), so the phone loses the visit rather than keeping it.
/// </para>
/// <para>
/// The job's own status is not touched: <c>Cancelled</c> is terminal and correct, unlike the
/// optimiser's withdrawal, which puts work back in the pile because it is still wanted.
/// </para>
/// <para>
/// A reaction is its own unit of work (<see cref="IUnitOfWork"/>): the cancellation has already
/// committed by the time this runs, so this save is a second transaction. A failure here fails the
/// request that cancelled the job — loudly, with the cancellation itself standing — which is the
/// right way round: the stop outliving the cancellation is visible on the board, and the alternative
/// is a swallowed error nobody sees.
/// </para>
/// </remarks>
internal sealed class WithdrawStopWhenJobIsCancelled(IAssignmentRepository assignments, IUnitOfWork unitOfWork)
    : IDomainEventHandler<JobCancelled>
{
    public async Task Handle(JobCancelled domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var stop = await assignments
            .GetByJobAsync(domainEvent.JobId, cancellationToken)
            .ConfigureAwait(false);

        // Work called off before anybody planned it. Nothing to withdraw, and nothing to say.
        if (stop is null)
        {
            return;
        }

        assignments.Remove(stop);

        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
