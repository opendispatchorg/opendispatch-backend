namespace OpenDispatch.Contracts.Technicians;

/// <summary>One technician, as the clients see them.</summary>
/// <param name="Id">Their identity.</param>
/// <param name="Name">Their name, as it appears on the dispatch board.</param>
/// <param name="Skills">What they are qualified to work on, alphabetically.</param>
/// <param name="ShiftStart">When their working hours open.</param>
/// <param name="ShiftEnd">When their working hours close.</param>
/// <param name="Latitude">Their home base, in decimal degrees.</param>
/// <param name="Longitude">Their home base, in decimal degrees.</param>
/// <param name="RetiredAt">
/// When they were taken off the books, or <see langword="null"/> while they are current. Retired
/// records are left out of the list, so a client that has one in hand — from a bookmark, an
/// export, or a job that predates the retirement — needs this to say why it looks inert, and to
/// know that reinstating is the way back.
/// </param>
public sealed record TechnicianResponse(
    Guid Id,
    string Name,
    IReadOnlyList<string> Skills,
    DateTimeOffset ShiftStart,
    DateTimeOffset ShiftEnd,
    double Latitude,
    double Longitude,
    DateTimeOffset? RetiredAt);
