namespace OpenDispatch.Contracts.Export;

/// <summary>One stop, as it appears in <c>GET /export</c>.</summary>
/// <param name="Id">The stop's identity.</param>
/// <param name="JobId">The job being planned.</param>
/// <param name="TechnicianId">Whose day it sits on.</param>
/// <param name="Sequence">Where it falls in the technician's run, counting from zero.</param>
/// <param name="ScheduledStart">When the technician is planned to start work.</param>
/// <param name="TravelMin">Minutes of driving to reach this stop from the previous one.</param>
public sealed record AssignmentExport(
    Guid Id,
    Guid JobId,
    Guid TechnicianId,
    int Sequence,
    DateTimeOffset ScheduledStart,
    double TravelMin);
