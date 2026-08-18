namespace OpenDispatch.Contracts.Schedule;

/// <summary>What <c>POST /schedule/normalize</c> did.</summary>
/// <param name="Stops">How many stops that technician has in the stretch.</param>
/// <param name="Moved">How many had to move. Zero means the day was already drivable.</param>
/// <param name="FirstStart">When the run now begins, or <see langword="null"/> if there is no work.</param>
/// <param name="LastEnd">When it now ends, or <see langword="null"/> if there is no work.</param>
/// <remarks>
/// <see cref="Moved"/> is the number a dispatcher reads: it says how much of what they were looking
/// at has just changed underneath them, which is the difference between "nothing to see" and "check
/// the afternoon".
/// </remarks>
public sealed record NormalizeDayResponse(int Stops, int Moved, DateTimeOffset? FirstStart, DateTimeOffset? LastEnd);
