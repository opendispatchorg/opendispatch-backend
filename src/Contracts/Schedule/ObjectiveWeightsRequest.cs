namespace OpenDispatch.Contracts.Schedule;

/// <summary>What a good schedule is worth, as a caller states it. Optional on <see cref="OptimizeScheduleRequest"/>.</summary>
/// <param name="Travel">Cost of one minute of driving. The unit the other three are quoted in.</param>
/// <param name="Lateness">Cost of one minute past a job's promised window.</param>
/// <param name="Overtime">Cost of one minute worked past the end of a technician's shift.</param>
/// <param name="Unassigned">Cost of leaving a job undone, per unit of its priority.</param>
/// <remarks>
/// A flat copy of the domain's <c>ObjectiveWeights</c>, not a reference to it — the same reason
/// every other request in this package holds primitives rather than value objects. Left out of the
/// request entirely, the engine's own defaults apply.
/// </remarks>
public sealed record ObjectiveWeightsRequest(double Travel, double Lateness, double Overtime, double Unassigned);
