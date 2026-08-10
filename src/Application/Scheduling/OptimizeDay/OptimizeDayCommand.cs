using OpenDispatch.Application.Messaging;

namespace OpenDispatch.Application.Scheduling.OptimizeDay;

/// <summary>
/// Re-plans a stretch of time: every job that can still be scheduled, laid out across the whole
/// crew as cheaply as the engine can manage.
/// </summary>
/// <param name="From">When the horizon opens.</param>
/// <param name="To">When it closes. Never earlier than <paramref name="From"/>.</param>
/// <param name="Weights">
/// What a good schedule is worth, or <see langword="null"/> for the engine's defaults.
/// </param>
/// <remarks>
/// <para>
/// The counterpart to a manual assignment: that one is told where a stop goes, this one works it
/// out. It rewrites the plan and leaves the demand alone — which is what the Job/Assignment split
/// is for — so running it twice over an unchanged day changes nothing.
/// </para>
/// <para>
/// A horizon rather than a date, because a date is not an instant until somebody names a timezone
/// and nothing in this system models one. Resolving "today" belongs to the caller, which knows
/// whose day it is asking about.
/// </para>
/// </remarks>
public sealed record OptimizeDayCommand(DateTimeOffset From, DateTimeOffset To, ObjectiveWeights? Weights = null)
    : ICommand<OptimizedDay>;
