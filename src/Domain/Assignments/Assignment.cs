using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Domain.Assignments;

/// <summary>
/// One stop in a technician's day: which job, whose day it is on, when it starts, where it
/// falls in the run, and how long the drive to it is.
/// </summary>
/// <remarks>
/// <para>
/// The assignment is the <em>plan</em>, and it is its own aggregate for one reason: the plan
/// is rewritten constantly — every manual drag on the board, every optimiser run — while the
/// demand underneath it does not move. Fusing the two would mean re-optimising a day mutates
/// every job in it.
/// </para>
/// <para>
/// It points at its job by <see cref="JobId"/> and holds no navigation property to
/// <c>Job</c>, deliberately. Rescheduling loads and saves an assignment; it must not drag a
/// job into the transaction, and a reader must not be able to reach through one aggregate
/// into another.
/// </para>
/// </remarks>
public sealed class Assignment : AggregateRoot
{
    // Materialisation constructor — see the note on Job. Every member here is a value type, so
    // there is nothing to placeholder.
    private Assignment()
    {
    }

    private Assignment(
        AssignmentId id,
        OrgId orgId,
        JobId jobId,
        TechnicianId technicianId,
        int sequence,
        DateTimeOffset scheduledStart,
        double travelMin)
    {
        Id = id;
        OrgId = orgId;
        JobId = jobId;
        TechnicianId = technicianId;
        Sequence = sequence;
        ScheduledStart = scheduledStart;
        TravelMin = travelMin;
    }

    /// <summary>This assignment's identity.</summary>
    public AssignmentId Id { get; private set; }

    /// <summary>The tenant this assignment belongs to.</summary>
    public OrgId OrgId { get; private set; }

    /// <summary>The job being planned. A reference, never a navigation property.</summary>
    public JobId JobId { get; private set; }

    /// <summary>Whose day the stop sits on.</summary>
    public TechnicianId TechnicianId { get; private set; }

    /// <summary>Where the stop falls in the technician's run for the day, counting from zero.</summary>
    public int Sequence { get; private set; }

    /// <summary>
    /// When the technician is planned to start work. May fall outside the job's promised window
    /// — lateness is a soft constraint the scheduler pays a penalty for, not a bar.
    /// </summary>
    /// <remarks>
    /// Work starting, not the technician arriving, and the two differ only when somebody reaches a
    /// site before the customer's window opens and waits. This is the instant a dispatcher enters
    /// by hand, the one the customer was promised, and the one lateness is measured at — so the
    /// manual and optimised paths write the same meaning here. How long anybody waited is the
    /// engine's business and does not survive into the plan.
    /// </remarks>
    public DateTimeOffset ScheduledStart { get; private set; }

    /// <summary>Minutes of driving to reach this stop from the previous one.</summary>
    public double TravelMin { get; private set; }

    /// <summary>
    /// Plans a job into a technician's day. Raises nothing: an assignment coming into
    /// existence is announced by the slice that created it, not by the aggregate.
    /// </summary>
    /// <exception cref="DomainException">The placement is not one a route can contain.</exception>
    public static Assignment Create(
        OrgId orgId,
        JobId jobId,
        TechnicianId technicianId,
        int sequence,
        DateTimeOffset scheduledStart,
        double travelMin)
    {
        ValidatePlacement(sequence, travelMin);

        return new Assignment(
            AssignmentId.New(),
            orgId,
            jobId,
            technicianId,
            sequence,
            scheduledStart,
            travelMin);
    }

    /// <summary>
    /// Moves the stop within the same technician's day — a drag on the board, or a slot the
    /// optimiser found.
    /// </summary>
    /// <exception cref="DomainException">The placement is not one a route can contain.</exception>
    public void Reschedule(DateTimeOffset start, int sequence, double travelMin)
    {
        ValidatePlacement(sequence, travelMin);

        ScheduledStart = start;
        Sequence = sequence;
        TravelMin = travelMin;

        Raise(new AssignmentChanged(Id, JobId, TechnicianId));
    }

    /// <summary>
    /// Hands the stop to a different technician. The time and sequence come from the old
    /// route and are stale until <see cref="Reschedule"/> settles them.
    /// </summary>
    public void Reassign(TechnicianId technicianId)
    {
        TechnicianId = technicianId;

        Raise(new AssignmentChanged(Id, JobId, TechnicianId));
    }

    private static void ValidatePlacement(int sequence, double travelMin)
    {
        if (sequence < 0)
        {
            throw new DomainException("A stop cannot sit at a negative position in a technician's day.");
        }

        // Finite as well as non-negative: travel minutes come out of a distance
        // calculation, and a NaN would spread silently through the objective function.
        if (!double.IsFinite(travelMin) || travelMin < 0d)
        {
            throw new DomainException("Travel to a stop must be a finite, non-negative number of minutes.");
        }
    }
}
