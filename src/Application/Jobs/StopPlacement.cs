using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Application.Jobs;

/// <summary>
/// Puts a job's one stop where it has been decided to go.
/// </summary>
/// <remarks>
/// <para>
/// Two slices plan work — a dispatcher dragging one stop, and the optimiser rewriting a whole day
/// — and both have to keep the same rule: <strong>a job has at most one stop</strong>. Moving one
/// is <c>Reassign</c> then <c>Reschedule</c>, never an add, and a unique index turns a mistake into
/// a failed insert rather than a second stop nobody can account for.
/// </para>
/// <para>
/// It is here rather than repeated in both because the rule is the invariant, not the arithmetic.
/// What each caller works out for itself is <em>where</em> the stop goes — a dispatcher is told,
/// the optimiser decides — and that difference is the whole of what separates the two slices.
/// </para>
/// </remarks>
internal static class StopPlacement
{
    /// <summary>
    /// Creates or moves the stop for a job, and turns demand into a plan the first time.
    /// </summary>
    /// <param name="assignments">The plan.</param>
    /// <param name="orgId">The tenant, for a stop that has to be created.</param>
    /// <param name="job">The work being planned. Transitioned to <c>Scheduled</c> if it was only demand.</param>
    /// <param name="technician">Whose day it goes on.</param>
    /// <param name="sequence">Where it falls in that technician's run.</param>
    /// <param name="start">When the technician is planned to arrive.</param>
    /// <param name="travelMin">Minutes of driving to reach it from whatever comes before.</param>
    /// <param name="ct">Cancels the load.</param>
    /// <returns>The stop, created or moved.</returns>
    public static async Task<Assignment> PlaceAsync(
        IAssignmentRepository assignments,
        OrgId orgId,
        Job job,
        TechnicianId technician,
        int sequence,
        DateTimeOffset start,
        double travelMin,
        CancellationToken ct)
    {
        var assignment = await assignments.GetByJobAsync(job.Id, ct).ConfigureAwait(false);

        if (assignment is null)
        {
            assignment = Assignment.Create(orgId, job.Id, technician, sequence, start, travelMin);
            assignments.Add(assignment);
        }
        else
        {
            // Two events for one move when the technician changes, which is what step 8 chose:
            // each method announces itself, and the board is told both that the stop changed
            // hands and that it changed time.
            if (assignment.TechnicianId != technician)
            {
                assignment.Reassign(technician);
            }

            assignment.Reschedule(start, sequence, travelMin);
        }

        // Planned work that was only demand until now. A job already Scheduled or Dispatched is
        // being moved rather than planned, and moving it must not push it round the state machine
        // — Dispatched in particular would go backwards.
        if (job.Status is JobStatus.Unscheduled)
        {
            job.Schedule();
        }

        return assignment;
    }
}
