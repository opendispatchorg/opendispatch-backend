using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Messaging;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Api.IntegrationTests.Events;

/// <summary>
/// A sample command that walks a dispatched job through to completion, then either stands by it
/// or refuses.
/// </summary>
/// <remarks>
/// Three transitions rather than one, so the events it raises have an order worth asserting.
/// Step 35 is the real version of this; a sample is what lets the dispatch mechanism be proved
/// without writing a slice a step early.
/// </remarks>
/// <param name="JobId">The job to finish.</param>
/// <param name="At">When the work actually finished.</param>
/// <param name="ThenRefuse">Whether to report a failure after finishing it.</param>
internal sealed record CompleteJobCommand(JobId JobId, DateTimeOffset At, bool ThenRefuse) : ICommand;

/// <summary>Drives the job through the domain's own intent methods and saves.</summary>
internal sealed class CompleteJobHandler(IJobRepository jobs, IUnitOfWork unitOfWork)
    : IRequestHandler<CompleteJobCommand, Result>
{
    /// <summary>The failure the handler reports when told to refuse.</summary>
    public static Error Refused { get; } =
        Error.Conflict("sample.refused", "The sample handler refused after completing the job.");

    public async Task<Result> Handle(CompleteJobCommand command, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(command.JobId, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return Result.Failure(Error.NotFound("job.notFound", "This tenant has no such job."));
        }

        job.MarkEnRoute();
        job.MarkInProgress();
        job.MarkCompleted(command.At);

        // Saved here even though the pipeline will save again, deliberately: the interceptor
        // therefore runs twice over the same aggregate, and "exactly once" becomes a claim about
        // the events being cleared as they are taken rather than about there having been only
        // one opportunity to collect them.
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return command.ThenRefuse ? Result.Failure(Refused) : Result.Success();
    }
}
