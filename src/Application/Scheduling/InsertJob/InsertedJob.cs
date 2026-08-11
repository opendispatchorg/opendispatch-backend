using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Scheduling.InsertJob;

/// <summary>
/// Where the emergency went.
/// </summary>
/// <param name="AssignmentId">The stop that now exists for it.</param>
/// <param name="TechnicianId">Whose day it landed on.</param>
/// <param name="ScheduledStart">When the work is planned to start.</param>
/// <param name="Sequence">Where it falls in that technician's run.</param>
/// <param name="Displaced">
/// How many of that technician's other stops had to move along to make room. Zero when the job
/// went on the end of a day.
/// </param>
/// <remarks>
/// The answer to the question the dispatcher actually asked — "where does this fit?" — rather than
/// an acknowledgement. <see cref="Displaced"/> is the part worth showing: it is the cost of the
/// emergency to everybody else's afternoon, and it is the number that tells a dispatcher whether
/// to warn anyone.
/// </remarks>
public sealed record InsertedJob(
    AssignmentId AssignmentId,
    TechnicianId TechnicianId,
    DateTimeOffset ScheduledStart,
    int Sequence,
    int Displaced);
