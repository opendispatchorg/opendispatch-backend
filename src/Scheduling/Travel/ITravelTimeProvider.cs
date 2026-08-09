using System.Diagnostics.CodeAnalysis;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Scheduling.Travel;

/// <summary>
/// How long it takes to drive between two places.
/// </summary>
/// <remarks>
/// <para>
/// The one thing the engine cannot work out for itself, and therefore the one seam it has.
/// The default implementation that ships with the engine is straight-line
/// (<see cref="HaversineTravelTimeProvider"/>) and needs no infrastructure at all; a real
/// road-network provider arrives later as an adapter in the Infrastructure layer, and the
/// solver does not change by a line.
/// </para>
/// <para>
/// Minutes rather than distance because minutes are what the objective adds up. A provider
/// that knows about traffic, one-way systems or a technician's van is free to return
/// something that is not proportional to distance at all — including an asymmetric answer,
/// since a real road network rarely costs the same in both directions.
/// </para>
/// <para>
/// Implementations must be safe to call from several threads at once and must return the same
/// answer for the same pair every time: the search calls this in its innermost loop and
/// compares schedules by cost, so a provider that drifts between calls makes two identical
/// plans score differently.
/// </para>
/// </remarks>
public interface ITravelTimeProvider
{
    /// <summary>Minutes of driving from one place to another. Never negative.</summary>
    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "The signature is fixed by Document 2 section 4 and is the contract the OSRM adapter will implement. The rule guards against VB implementers, which a C#-only backend does not have.")]
    double Minutes(GeoPoint from, GeoPoint to);

    /// <summary>
    /// Every pairwise driving time among <paramref name="points"/>, indexed
    /// <c>[from][to]</c> in the order given.
    /// </summary>
    /// <remarks>
    /// Present as well as <see cref="Minutes"/> because a road-network provider answers one
    /// batched request far more cheaply than n² single ones. The search builds the matrix
    /// once per solve and reads it thereafter.
    /// </remarks>
    double[][] Matrix(IReadOnlyList<GeoPoint> points);
}
