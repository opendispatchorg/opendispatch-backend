namespace OpenDispatch.Contracts.Technicians;

/// <summary>One technician, as the clients see them.</summary>
/// <param name="Id">Their identity.</param>
/// <param name="Name">Their name, as it appears on the dispatch board.</param>
/// <param name="Skills">What they are qualified to work on, alphabetically.</param>
/// <param name="ShiftStart">When their working hours open.</param>
/// <param name="ShiftEnd">When their working hours close.</param>
/// <param name="Latitude">Their home base, in decimal degrees.</param>
/// <param name="Longitude">Their home base, in decimal degrees.</param>
public sealed record TechnicianResponse(
    Guid Id,
    string Name,
    IReadOnlyList<string> Skills,
    DateTimeOffset ShiftStart,
    DateTimeOffset ShiftEnd,
    double Latitude,
    double Longitude);
