using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Scheduling;

/// <summary>
/// The expected failures the planning slices can report.
/// </summary>
/// <remarks>
/// All three are conflicts rather than validation failures: the request is well-formed and the job
/// exists — the day is not in the state the request assumed. Step 46 answers them as 409.
/// </remarks>
public static class SchedulingErrors
{
    /// <summary>The code an insert carries when the job is already on somebody's day.</summary>
    public const string AlreadyPlannedCode = "job.alreadyPlanned";

    /// <summary>The code an insert carries when the job will not fit anywhere.</summary>
    public const string CouldNotPlaceCode = "job.couldNotBePlaced";

    /// <summary>The code an insert carries when the day it was asked to slot into is not a route.</summary>
    public const string OverlappingDayCode = "schedule.overlappingDay";

    /// <summary>
    /// Reports a job that already has a stop.
    /// </summary>
    /// <remarks>
    /// Refused rather than treated as a no-op. A caller asking to slot in work that is already on
    /// the board has lost track of what the day looks like, and answering "done" would let them
    /// carry on believing it. Re-placing it is a re-optimise or a drag, both of which say so.
    /// </remarks>
    /// <param name="id">The job that was asked for.</param>
    public static Error AlreadyPlanned(JobId id) =>
        Error.Conflict(AlreadyPlannedCode, $"Job {id.Value} is already on somebody's day.");

    /// <summary>
    /// Reports work that will not fit into the day as it stands.
    /// </summary>
    /// <remarks>
    /// Lateness is soft, so this is nearly always a hard constraint biting: nobody on shift holds
    /// the skill, or every technician's day is already full to the end of their hours. It is a
    /// failure rather than a success carrying an empty answer, because the caller asked for a slot
    /// and there is none — and because failing rolls the transaction back, so a refused emergency
    /// leaves the board exactly as it was.
    /// </remarks>
    /// <param name="id">The job that could not be placed.</param>
    public static Error CouldNotPlace(JobId id) =>
        Error.Conflict(
            CouldNotPlaceCode,
            $"Job {id.Value} will not fit into the day as it stands. Re-optimising or freeing a technician may make room.");

    /// <summary>
    /// Reports a technician whose planned day is not something anybody could drive.
    /// </summary>
    /// <remarks>
    /// A hand-dragged day can have two stops on top of each other — the manual path is a
    /// dispatcher's instruction and does not re-time the run around it — and a day like that
    /// cannot be expressed as a route to insert into. Saying so beats quietly planning an
    /// emergency around a day the engine has silently reinterpreted.
    /// </remarks>
    /// <param name="technician">Whose day overlaps itself.</param>
    public static Error OverlappingDay(TechnicianId technician) =>
        Error.Conflict(
            OverlappingDayCode,
            $"Technician {technician.Value} has overlapping stops, so there is no run to slot work into. Re-optimise the day first.");
}
