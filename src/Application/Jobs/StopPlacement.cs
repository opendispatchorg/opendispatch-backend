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
    /// <summary>Ticks in one microsecond — the finest thing a <c>timestamptz</c> column can hold.</summary>
    private const long TicksPerMicrosecond = TimeSpan.TicksPerMicrosecond;

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
        start = Storable(start);

        var assignment = await assignments.GetByJobAsync(job.Id, ct).ConfigureAwait(false);

        if (assignment is null)
        {
            assignment = Assignment.Create(orgId, job.Id, technician, sequence, start, travelMin);
            assignments.Add(assignment);
        }
        else if (HasMoved(assignment, technician, sequence, start, travelMin))
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

    /// <summary>
    /// The instant as the database will hold it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Postgres stores a <c>timestamptz</c> to the microsecond and .NET counts in ticks, which are
    /// ten times finer — so an instant that came out of the engine's arithmetic (travel is a
    /// <c>double</c> of minutes, and a fraction of a minute is rarely a whole microsecond) is not
    /// the instant that comes back out of the database.
    /// </para>
    /// <para>
    /// That is not a cosmetic difference. The plan is written by comparing what a stop should be
    /// against what it already is, and the value it already is has been through the database:
    /// without this, a recomputed identical plan differs by a few hundred nanoseconds, every stop
    /// looks moved, and re-optimising an unchanged day rewrites and re-announces all of it. So the
    /// instant is truncated to what can be stored <em>before</em> it reaches the domain, and both
    /// sides of the comparison mean the same thing.
    /// </para>
    /// </remarks>
    private static DateTimeOffset Storable(DateTimeOffset instant) =>
        instant.AddTicks(-(instant.Ticks % TicksPerMicrosecond));

    /// <summary>
    /// Whether the stop is being asked to go anywhere it is not already.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stop told to stay exactly where it is stays silent. It matters because both callers
    /// rewrite whole days: re-optimising an unchanged day would otherwise raise an
    /// <c>AssignmentChanged</c> for every stop on it and repaint a board on which nothing has
    /// moved, and an emergency insert is supposed to leave the rest of the plan alone rather than
    /// announce that it did.
    /// </para>
    /// <para>
    /// Travel is compared exactly, and that is deliberate: it comes back out of the same
    /// arithmetic over the same two points, so a difference means the plan genuinely changed
    /// rather than that a double drifted.
    /// </para>
    /// </remarks>
    private static bool HasMoved(
        Assignment assignment,
        TechnicianId technician,
        int sequence,
        DateTimeOffset start,
        double travelMin) =>
        assignment.TechnicianId != technician
        || assignment.Sequence != sequence
        || assignment.ScheduledStart != start
        || assignment.TravelMin != travelMin;
}
