namespace OpenDispatch.Contracts.Schedule;

/// <summary>The response from <c>POST /schedule/optimize</c>: what the optimiser did.</summary>
/// <param name="Planned">How many stops the day now has.</param>
/// <param name="Unassigned">The jobs no technician could take. Not a failure — see the remarks on <c>OptimizedDay</c>.</param>
/// <param name="Cost">
/// What the plan scores under the weights it was given. Only comparable against another response
/// for the same horizon under the same weights.
/// </param>
public sealed record OptimizeScheduleResponse(int Planned, IReadOnlyList<Guid> Unassigned, double Cost);
