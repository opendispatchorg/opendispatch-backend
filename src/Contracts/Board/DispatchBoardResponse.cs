namespace OpenDispatch.Contracts.Board;

/// <summary>The response from <c>GET /dispatch/board?day=</c>: a snapshot of the day.</summary>
/// <param name="DayStart">The instant the requested day opens, UTC.</param>
/// <param name="DayEnd">The instant the requested day closes, UTC.</param>
/// <param name="Routes">One run per technician, each ordered by sequence. Technicians with nothing on are present and empty.</param>
/// <param name="Unassigned">Jobs promised inside the day that no technician has been given.</param>
/// <remarks>
/// A snapshot, stale the moment it is fetched — a client that wants to stay current applies the
/// SignalR deltas (<see cref="BoardEvents"/>) to what this call returned rather than re-fetching it,
/// which is also why this shape reshapes nothing from the read model it wraps.
/// </remarks>
public sealed record DispatchBoardResponse(
    DateTimeOffset DayStart,
    DateTimeOffset DayEnd,
    IReadOnlyList<BoardRouteResponse> Routes,
    IReadOnlyList<BoardJobResponse> Unassigned);
