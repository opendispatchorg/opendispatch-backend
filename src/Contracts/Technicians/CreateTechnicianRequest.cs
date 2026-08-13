namespace OpenDispatch.Contracts.Technicians;

/// <summary>The body of <c>POST /technicians</c>.</summary>
/// <param name="Name">Their name, as it appears on the dispatch board.</param>
/// <param name="Skills">What they are qualified to work on. Empty is allowed — a trainee simply matches no skilled job.</param>
/// <param name="ShiftStart">When their working hours open.</param>
/// <param name="ShiftEnd">When their working hours close.</param>
/// <param name="Latitude">Their home base, in decimal degrees between -90 and 90.</param>
/// <param name="Longitude">Their home base, in decimal degrees between -180 and 180.</param>
public sealed record CreateTechnicianRequest(
    string Name,
    IReadOnlyList<string> Skills,
    DateTimeOffset ShiftStart,
    DateTimeOffset ShiftEnd,
    double Latitude,
    double Longitude);
