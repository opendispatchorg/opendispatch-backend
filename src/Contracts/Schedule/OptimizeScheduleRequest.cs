namespace OpenDispatch.Contracts.Schedule;

/// <summary>The body of <c>POST /schedule/optimize</c>.</summary>
/// <param name="From">When the horizon to re-plan opens.</param>
/// <param name="To">When it closes. Never earlier than <paramref name="From"/>.</param>
/// <param name="Weights">What a good schedule is worth, or <see langword="null"/> for the engine's defaults.</param>
/// <remarks>
/// A horizon rather than a date, matching the command it wraps
/// (<c>OptimizeDayCommand</c>) exactly: a date is not an instant until somebody names a timezone,
/// and nothing in this system models one. Resolving "today" belongs to the caller.
/// </remarks>
public sealed record OptimizeScheduleRequest(
    DateTimeOffset From,
    DateTimeOffset To,
    ObjectiveWeightsRequest? Weights = null);
