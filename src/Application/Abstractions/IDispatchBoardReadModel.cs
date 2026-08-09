using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Reads the dispatch board for a day.
/// </summary>
/// <remarks>
/// <para>
/// A read port, not a repository, and the distinction is the whole reason it exists. The
/// board shows every technician's day at once with a customer name against each stop —
/// assembling that from aggregates would mean loading every job, every assignment and every
/// customer in the org, through repositories built for changing one thing at a time, and
/// then throwing nine tenths of it away. This asks the database one question and gets back
/// exactly what is drawn.
/// </para>
/// <para>
/// It returns a projection (<see cref="DispatchBoard"/>) and no aggregate ever appears in
/// it. That is not tidiness: an aggregate handed to a caller that cannot save it is a trap,
/// and one handed to a caller that <em>can</em> is a second way to change data that bypasses
/// the repositories.
/// </para>
/// <para>
/// The board is also the payload the SignalR events are deltas against, so the shape here
/// and the board-event shapes in <c>Contracts</c> have to stay recognisably the same thing.
/// </para>
/// </remarks>
public interface IDispatchBoardReadModel
{
    /// <summary>
    /// Fetches the board for a day.
    /// </summary>
    /// <remarks>
    /// The day is a <see cref="TimeWindow"/> rather than a date because a date is not an
    /// instant until somebody says which timezone it is in, and the only place that knows is
    /// the edge the request came from. Stops are selected by scheduled start; unassigned
    /// jobs by their promised window.
    /// </remarks>
    /// <returns>The board, empty of stops if nothing is planned. Never <see langword="null"/>.</returns>
    Task<DispatchBoard> GetAsync(TimeWindow day, CancellationToken ct);
}
