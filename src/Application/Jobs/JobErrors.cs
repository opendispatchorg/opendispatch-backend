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

    /// <summary>The code every refused attempt to plan a job carries.</summary>
    /// <remarks>
    /// Distinct from an illegal transition, because it is a different question: not "may the job
    /// move from here to there" but "may this job be planned at all". A job somebody is already
    /// driving to cannot be dragged onto another technician's afternoon, and neither can one that
    /// was finished or called off.
    /// </remarks>
    public const string NotSchedulableCode = "job.notSchedulable";

    /// <summary>The code every refusal to write to an erased job carries.</summary>
    public const string ErasedCode = "job.erased";

    /// <summary>Names a job this tenant does not have.</summary>
    /// <param name="id">The job that was asked for.</param>
    public static Error NotFound(JobId id) =>
        Error.NotFound(NotFoundCode, $"There is no job {id.Value}.");

    /// <summary>
    /// Reports a job whose customer has been erased, so nothing more may be written about it.
    /// </summary>
    /// <remarks>
    /// The job is still there and still bills for what it billed; what it may no longer accept is
    /// anything that says something about a person — a photograph of the house, a note about the
    /// visit. A phone that was holding the job offline when the erasure ran is the caller this is
    /// actually written for, and it is a conflict rather than a miss because nothing is missing.
    /// </remarks>
    /// <param name="id">The job that was written to.</param>
    public static Error Erased(JobId id) =>
        Error.Conflict(ErasedCode, $"Job {id.Value} belongs to an erased customer.");

    /// <summary>Reports a job that can no longer be planned into anybody's day.</summary>
    /// <param name="status">Where the job actually is.</param>
    /// <remarks>
    /// Which statuses those are is <c>Job.SchedulableStatuses</c>' answer, not this slice's — the
    /// optimiser, the emergency insert and this path all have to agree on which work may be moved,
    /// so the question is asked of the domain and only reported here.
    /// </remarks>
    public static Error NotSchedulable(JobStatus status) =>
        Error.Conflict(NotSchedulableCode, $"A job that is {status} can no longer be planned.");

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
