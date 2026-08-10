using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Application.Jobs;

/// <summary>
/// The expected failures the Jobs slices can report.
/// </summary>
public static class JobErrors
{
    /// <summary>The code every "no such job" failure carries.</summary>
    public const string NotFoundCode = "job.notFound";

    /// <summary>The code every refused status change carries.</summary>
    /// <remarks>
    /// <para>
    /// A conflict rather than a validation failure, and the distinction is the whole shape of this
    /// answer: the request was well-formed and the job exists — the world was simply not in the
    /// state the caller assumed. Step 46 turns that into a 409.
    /// </para>
    /// <para>
    /// This is the shape step 42's sync conflicts inherit. A phone that pushes a status op for a
    /// job somebody else has already moved is asking exactly this question over a different
    /// transport, and it must not get a different answer.
    /// </para>
    /// </remarks>
    public const string IllegalTransitionCode = "job.illegalTransition";

    /// <summary>Names a job this tenant does not have.</summary>
    /// <param name="id">The job that was asked for.</param>
    public static Error NotFound(JobId id) =>
        Error.NotFound(NotFoundCode, $"There is no job {id.Value}.");

    /// <summary>Reports a move the state machine does not allow.</summary>
    /// <param name="from">Where the job actually is.</param>
    /// <param name="to">Where the caller wanted it.</param>
    /// <remarks>
    /// The message names both ends because the caller's whole problem is that it believed the job
    /// was somewhere else — a dispatcher looking at a stale board, or a phone that has been in a
    /// basement. Telling it only that the move was refused leaves it no way to correct itself.
    /// </remarks>
    public static Error IllegalTransition(JobStatus from, JobStatus to) =>
        Error.Conflict(IllegalTransitionCode, $"A job that is {from} cannot become {to}.");
}
