using System.Collections.Frozen;
using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Scheduling.Model;

/// <summary>
/// A plan for the horizon: each technician's day as an ordered run of stops, the jobs nobody
/// could take, and what the whole thing costs.
/// </summary>
/// <remarks>
/// <para>
/// The engine's entire output. It carries no aggregates and no database identity — turning it
/// into <c>Assignment</c>s is the caller's job, and the engine never learns whether that
/// happened.
/// </para>
/// <para>
/// <see cref="Unassigned"/> is a first-class part of the answer rather than a failure. Soft
/// lateness means a job is almost never unschedulable for want of <em>time</em>; a job lands
/// here because a hard constraint bit — nobody holds the skill, or no shift can contain it —
/// and a dispatcher needs to see that, not an exception.
/// </para>
/// <para>
/// The constructor enforces what it can see: a job is placed at most once, a placed job is not
/// also reported undone, and a technician's stops run in order without overlapping. Those are
/// structural — a plan that breaks one is not a worse plan, it is not a plan. Skill match and
/// shift containment are <em>not</em> checked, because they need the problem this solution
/// answers, which it deliberately does not hold.
/// </para>
/// </remarks>
public sealed class Solution
{
    /// <summary>States a plan.</summary>
    /// <param name="routes">
    /// Each technician's ordered run of stops. Technicians with nothing to do may be omitted
    /// or present with an empty route; both mean the same thing.
    /// </param>
    /// <param name="unassigned">Jobs no technician could take.</param>
    /// <param name="cost">What the plan scores under the problem's weights.</param>
    /// <exception cref="ArgumentException">
    /// A job is placed twice, a placed job is also reported unassigned, or a route doubles
    /// back on itself in time.
    /// </exception>
    public Solution(
        IReadOnlyDictionary<TechnicianId, ImmutableArray<Stop>> routes,
        IEnumerable<JobId> unassigned,
        double cost)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(unassigned);

        if (!double.IsFinite(cost) || cost < 0d)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cost), cost, "The cost of a plan must be finite and non-negative.");
        }

        var placed = new HashSet<JobId>();

        foreach (var (technician, route) in routes)
        {
            // A default ImmutableArray is not an empty one — it has no backing array at all
            // and throws on any use. Catching it here beats a NullReferenceException three
            // steps deeper into the search.
            if (route.IsDefault)
            {
                throw new ArgumentException(
                    $"Technician {technician.Value} has no route at all. A technician with nothing to do has an empty one.",
                    nameof(routes));
            }

            for (var i = 0; i < route.Length; i++)
            {
                if (i > 0 && route[i].Arrival < route[i - 1].End)
                {
                    throw new ArgumentException(
                        $"Technician {technician.Value} would be in two places at once: stop {i} begins before stop {i - 1} ends.",
                        nameof(routes));
                }

                if (!placed.Add(route[i].JobId))
                {
                    throw new ArgumentException(
                        $"Job {route[i].JobId.Value} is placed more than once.", nameof(routes));
                }
            }
        }

        var dropped = ImmutableArray.CreateRange(unassigned);
        var seenUnassigned = new HashSet<JobId>();

        foreach (var job in dropped)
        {
            if (placed.Contains(job))
            {
                throw new ArgumentException(
                    $"Job {job.Value} is both placed and reported unassigned.", nameof(unassigned));
            }

            if (!seenUnassigned.Add(job))
            {
                throw new ArgumentException(
                    $"Job {job.Value} is reported unassigned more than once.", nameof(unassigned));
            }
        }

        Routes = routes.ToFrozenDictionary();
        Unassigned = dropped;
        Cost = cost;
    }

    /// <summary>Each technician's day, in the order they will drive it.</summary>
    public FrozenDictionary<TechnicianId, ImmutableArray<Stop>> Routes { get; }

    /// <summary>The jobs no technician could take.</summary>
    public ImmutableArray<JobId> Unassigned { get; }

    /// <summary>What this plan scores under the weights of the problem it answers.</summary>
    public double Cost { get; }

    /// <summary>
    /// One technician's day. Empty for a technician with nothing to do, so a caller never has
    /// to ask whether they are in the dictionary at all.
    /// </summary>
    public ImmutableArray<Stop> RouteFor(TechnicianId technician) =>
        Routes.TryGetValue(technician, out var route) ? route : [];
}
