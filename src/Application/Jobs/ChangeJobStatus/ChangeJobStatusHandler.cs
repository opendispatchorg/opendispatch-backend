using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Jobs.ChangeJobStatus;

/// <summary>
/// Loads the job, asks whether the move is legal, and lets the domain make it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It asks rather than catching.</strong> <c>Job.CanTransition</c> answers the question
/// <c>Transition</c> would answer by throwing, so an illegal move becomes a failure
/// <c>Result</c> without an exception being used for something entirely expected. The alternative
/// — calling the intent method inside a <c>try</c> and turning <c>DomainException</c> into an
/// error — would also catch invariants that have nothing to do with the state machine and report
/// them as if they did.
/// </para>
/// <para>
/// Asking does not move the rule out of the domain. The table is the domain's, the question is
/// answered by the aggregate, and the intent method still enforces it: a handler that skipped the
/// check would get an exception, not an illegal job.
/// </para>
/// <para>
/// Nothing here sets a status, and there is no path that could. The command names a destination
/// and <see cref="JobIntents.Drivable"/> maps it to the method that means it, so events and
/// legality both come from the aggregate.
/// </para>
/// </remarks>
internal sealed class ChangeJobStatusHandler(IJobRepository jobs, IClock clock)
    : IRequestHandler<ChangeJobStatusCommand, Result>
{
    public async Task<Result> Handle(ChangeJobStatusCommand command, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(command.JobId, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return Result.Failure(JobErrors.NotFound(command.JobId));
        }

        if (!job.CanTransition(command.Status))
        {
            return Result.Failure(JobErrors.IllegalTransition(job.Status, command.Status));
        }

        // The validator has already refused anything this table does not cover, so the lookup
        // cannot miss. Work finished before it was reported unless the caller says otherwise.
        JobIntents.Drivable[command.Status](job, command.CompletedAt ?? clock.UtcNow);

        return Result.Success();
    }
}
