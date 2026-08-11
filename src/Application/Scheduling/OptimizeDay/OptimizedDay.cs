using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Scheduling.OptimizeDay;

/// <summary>
/// What the optimiser did: how much work it placed, what it could not place, and what the plan
/// costs.
/// </summary>
/// <param name="Planned">How many stops the day now has.</param>
/// <param name="Unassigned">
/// The jobs no technician could take. Not a failure — a first-class part of the answer.
/// </param>
/// <param name="Cost">What the plan scores under the weights it was given.</param>
/// <remarks>
/// <para>
/// <see cref="Unassigned"/> is the field this projection exists for. Lateness is soft, so a job is
/// almost never dropped for want of time: it lands here because a hard constraint bit — nobody
/// holds the skill, or no shift can contain it — and a dispatcher has to see that rather than
/// wonder why the board looks short.
/// </para>
/// <para>
/// <see cref="Cost"/> is in travel minutes and is only comparable against another plan for the
/// same problem under the same weights. It is here because two runs producing the same cost is how
/// a caller can tell nothing moved.
/// </para>
/// </remarks>
public sealed record OptimizedDay(int Planned, IReadOnlyList<JobId> Unassigned, double Cost);
