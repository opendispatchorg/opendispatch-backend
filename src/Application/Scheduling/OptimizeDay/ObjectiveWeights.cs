namespace OpenDispatch.Application.Scheduling.OptimizeDay;

/// <summary>
/// What a good schedule is worth, as a caller states it.
/// </summary>
/// <param name="Travel">Cost of one minute of driving. The unit the other three are quoted in.</param>
/// <param name="Lateness">Cost of one minute past a job's promised window.</param>
/// <param name="Overtime">Cost of one minute worked past the end of a technician's shift.</param>
/// <param name="Unassigned">Cost of leaving a job undone, per unit of its priority.</param>
/// <remarks>
/// <para>
/// The engine's <c>Objective</c> refuses a negative or infinite weight by throwing — a negative
/// price turns a penalty into a reward, and the search would happily run every job late — so a
/// command carrying one would be constructed at the edge, before any validator ran, and a mistyped
/// weight would arrive as an unhandled exception rather than a rejected field. This is the same
/// rule the coordinates and the windows follow, applied to the last value object that can refuse.
/// </para>
/// <para>
/// Weights are ratios, not absolutes: doubling all four changes nothing about which schedule wins.
/// Omitting them entirely is the ordinary case, and gets the engine's own defaults.
/// </para>
/// </remarks>
public sealed record ObjectiveWeights(double Travel, double Lateness, double Overtime, double Unassigned);
