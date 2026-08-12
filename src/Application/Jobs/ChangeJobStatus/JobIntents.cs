using System.Collections.Frozen;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Application.Jobs.ChangeJobStatus;

/// <summary>
/// The statuses a request can drive a job to, and the intent method that does it.
/// </summary>
/// <remarks>
/// <para>
/// One table read by two things that would otherwise disagree: the validator, which refuses a
/// target this request cannot drive, and the handler, which drives it. Written as a switch in the
/// handler and a list in the validator, they would be two statements of the same fact and the
/// second one to change would be wrong.
/// </para>
/// <para>
/// It is deliberately narrower than <see cref="Job.AllowedTransitions"/>, and the gap is the
/// interesting part. <c>Invoiced</c> and <c>Paid</c> are legal moves that no actor makes: they are
/// consequences of invoicing (step 40), which will drive them through the invoice aggregate.
/// <c>Unscheduled</c> is where a job starts, and the optimiser is the only thing that puts one back
/// there — withdrawing a plan it can no longer keep, which is not a button anybody presses. So the
/// exported transition
/// table is not wrong to include them — a client offering an "invoice" button because
/// <c>canTransition</c> said yes is a client bug — but this request refuses them, and says so as a
/// malformed request rather than as a conflict, because it is the request that is wrong and not
/// the state of the world.
/// </para>
/// </remarks>
internal static class JobIntents
{
    /// <summary>
    /// Each drivable status, and how a job is asked to reach it.
    /// </summary>
    /// <remarks>
    /// The instant is only read by completion — work finished before it was reported, so the
    /// domain has to be told when — and ignored by the rest. Passing it to all of them keeps this
    /// one table rather than one table and an exception.
    /// </remarks>
    internal static readonly FrozenDictionary<JobStatus, Action<Job, DateTimeOffset>> Drivable =
        new Dictionary<JobStatus, Action<Job, DateTimeOffset>>
        {
            [JobStatus.Scheduled] = (job, _) => job.Schedule(),
            [JobStatus.Dispatched] = (job, _) => job.Dispatch(),
            [JobStatus.EnRoute] = (job, _) => job.MarkEnRoute(),
            [JobStatus.InProgress] = (job, _) => job.MarkInProgress(),
            [JobStatus.Completed] = (job, at) => job.MarkCompleted(at),
            [JobStatus.Cancelled] = (job, _) => job.Cancel(),
        }.ToFrozenDictionary();
}
