using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// A day on the dispatch board: what every technician is doing, and what nobody is doing.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot, and stale the moment it is taken — the board keeps up by applying SignalR
/// deltas to it rather than by fetching it again.
/// </para>
/// <para>
/// The unassigned pile is a first-class half of the answer, exactly as it is in the
/// scheduler's <c>Solution</c>. Soft lateness means work is rarely unschedulable for want of
/// time, so a job sitting here is a hard constraint biting — nobody holds the skill, or no
/// shift can contain it — and it is the thing a dispatcher most needs to see.
/// </para>
/// </remarks>
/// <param name="Day">The stretch of time the board covers.</param>
/// <param name="Routes">One run per technician, each ordered by sequence. Technicians with nothing on are present and empty.</param>
/// <param name="Unassigned">Jobs promised inside the day that no technician has been given.</param>
public sealed record DispatchBoard(
    TimeWindow Day,
    IReadOnlyList<BoardRoute> Routes,
    IReadOnlyList<BoardJob> Unassigned);
