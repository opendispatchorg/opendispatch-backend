using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Export;

/// <summary>
/// One stop, as a reader sees it — <see cref="Domain.Assignments.Assignment"/>'s own fields and
/// nothing else, since it holds no navigation properties to project away.
/// </summary>
/// <param name="Id">The stop's identity.</param>
/// <param name="JobId">The job being planned.</param>
/// <param name="TechnicianId">Whose day it sits on.</param>
/// <param name="Sequence">Where it falls in the technician's run, counting from zero.</param>
/// <param name="ScheduledStart">When the technician is planned to start work.</param>
/// <param name="TravelMin">Minutes of driving to reach this stop from the previous one.</param>
/// <remarks>
/// Lives here rather than beside a dedicated Assignments slice because there is not one — every
/// other reader of a stop reaches it through <c>BoardStop</c> or a scheduling command instead.
/// <c>GetExportQuery</c> (step 49) is the first thing that wants the plan on its own, flat and
/// whole, which is what this shape is for.
/// </remarks>
public sealed record AssignmentSummary(
    AssignmentId Id,
    JobId JobId,
    TechnicianId TechnicianId,
    int Sequence,
    DateTimeOffset ScheduledStart,
    double TravelMin);
