using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.Scheduling.Search;

/// <summary>
/// What a move would do to a schedule, worked out but not yet done.
/// </summary>
/// <remarks>
/// The middle step of propose-inspect-take. A search asks <see cref="SearchState.Try"/> for
/// this, looks at <see cref="Cost"/>, and either hands it back to
/// <see cref="SearchState.Commit"/> or drops it on the floor. Nothing has changed until it is
/// committed, which is why the search needs no way to undo.
/// </remarks>
/// <param name="Cost">What the whole schedule would cost afterwards.</param>
/// <param name="Rewrites">The days that would change. Every other day is untouched.</param>
internal sealed record MoveOutcome(Cost Cost, ImmutableArray<RouteRewrite> Rewrites);

/// <summary>One technician's day as a move would leave it: the new order, timed and priced.</summary>
/// <param name="Technician">Whose day this is.</param>
/// <param name="Run">The order they would do it in.</param>
/// <param name="Stops">That order timed out.</param>
/// <param name="Cost">What the day would cost.</param>
internal sealed record RouteRewrite(
    TechnicianId Technician,
    List<SchedJob> Run,
    ImmutableArray<Stop> Stops,
    Cost Cost);
