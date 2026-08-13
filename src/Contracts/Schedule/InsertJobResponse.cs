namespace OpenDispatch.Contracts.Schedule;

/// <summary>The response from <c>POST /schedule/insert</c>: where the emergency went.</summary>
/// <param name="AssignmentId">The stop that now exists for it.</param>
/// <param name="TechnicianId">Whose day it landed on.</param>
/// <param name="ScheduledStart">When the work is planned to start.</param>
/// <param name="Sequence">Where it falls in that technician's run, counting from zero.</param>
/// <param name="Displaced">
/// How many of that technician's other stops had to move along to make room. Zero when the job
/// went on the end of a day.
/// </param>
public sealed record InsertJobResponse(
    Guid AssignmentId,
    Guid TechnicianId,
    DateTimeOffset ScheduledStart,
    int Sequence,
    int Displaced);
