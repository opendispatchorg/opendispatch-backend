namespace OpenDispatch.Contracts.Schedule;

/// <summary>
/// The body of <c>POST /schedule/normalize</c>: whose day to re-time, and over what stretch.
/// </summary>
/// <param name="TechnicianId">Whose day.</param>
/// <param name="From">When the stretch being repaired opens.</param>
/// <param name="To">When it closes.</param>
/// <remarks>
/// One technician rather than the whole board, which is the point: a re-plan reshuffles everybody's
/// day, and a dispatcher whose morning has one broken run does not want the other seven rewritten.
/// </remarks>
public sealed record NormalizeDayRequest(Guid TechnicianId, DateTimeOffset From, DateTimeOffset To);
