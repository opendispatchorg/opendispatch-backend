namespace OpenDispatch.Contracts.Board;

/// <summary>One technician's day, as it appears on the board over REST: a lane and a line on the map.</summary>
/// <param name="TechnicianId">Whose day it is.</param>
/// <param name="Name">Their name, as the lane is labelled.</param>
/// <param name="Skills">
/// What they are qualified for. Here for the same reason it is on the projection this mirrors: a
/// manual assignment may put work on somebody who does not hold the job's skill, and the board is
/// what shows the mismatch.
/// </param>
/// <param name="ShiftStart">When their working hours begin.</param>
/// <param name="ShiftEnd">When their working hours end.</param>
/// <param name="HomeLat">Where the day starts and ends, in decimal degrees.</param>
/// <param name="HomeLng">Where the day starts and ends, in decimal degrees.</param>
/// <param name="Stops">Their run, in sequence order.</param>
public sealed record BoardRouteResponse(
    Guid TechnicianId,
    string Name,
    IReadOnlyList<string> Skills,
    DateTimeOffset ShiftStart,
    DateTimeOffset ShiftEnd,
    double HomeLat,
    double HomeLng,
    IReadOnlyList<BoardStopResponse> Stops);
