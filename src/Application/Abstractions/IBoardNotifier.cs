using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Pushes a change to the dispatch board's live subscribers (Document 2 §9, step 51).
/// </summary>
/// <remarks>
/// <para>
/// No <see cref="OrgId"/> on either method, for the same reason no repository takes one:
/// <see cref="ITenantContext"/> is ambient, and an implementation reads it itself rather than
/// trusting a caller to pass the right tenant. <c>PortTests.ExactlyOnePortSaysWhoseDataItIs</c>
/// is what keeps that true.
/// </para>
/// <para>
/// Only two methods, not three. Document 2 §9 names a third event, <c>technician.moved</c>, but
/// nothing in this system tracks a technician's live position and nothing should (step 21's own
/// entry on <c>TechnicianMoved</c>) — there is no caller for a third method, and one here with
/// nothing to call it would be an abstraction built ahead of the data that would justify it. The
/// wire shape and the event name stay defined in <c>Contracts</c> for whenever a real location
/// source exists; this port gains the method the day something calls it.
/// </para>
/// </remarks>
public interface IBoardNotifier
{
    /// <summary>
    /// Announces that a job changed — Document 2 §9's <c>job.updated</c>.
    /// </summary>
    /// <param name="jobId">Which job.</param>
    /// <param name="status">What it is now.</param>
    /// <param name="version">Its concurrency stamp after the change.</param>
    /// <param name="ct">Cancellation.</param>
    Task JobUpdatedAsync(JobId jobId, JobStatus status, long version, CancellationToken ct);

    /// <summary>
    /// Announces that a stop's plan changed — Document 2 §9's <c>assignment.updated</c>.
    /// </summary>
    /// <param name="assignmentId">Which stop.</param>
    /// <param name="jobId">The job being planned.</param>
    /// <param name="technicianId">Whose lane it is now on.</param>
    /// <param name="sequence">Where it falls in that technician's run.</param>
    /// <param name="scheduledStart">When the technician is planned to arrive.</param>
    /// <param name="travelMin">Minutes of driving to reach it from the previous stop.</param>
    /// <param name="version">The stop's concurrency stamp after the change.</param>
    /// <param name="ct">Cancellation.</param>
    Task AssignmentUpdatedAsync(
        AssignmentId assignmentId,
        JobId jobId,
        TechnicianId technicianId,
        int sequence,
        DateTimeOffset scheduledStart,
        double travelMin,
        long version,
        CancellationToken ct);
}
