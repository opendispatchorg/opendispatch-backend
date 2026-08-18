using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.Scheduling.Travel;

namespace OpenDispatch.Scheduling;

/// <summary>
/// Puts a clock on a run somebody else decided the order of.
/// </summary>
/// <remarks>
/// <para>
/// The repair operation, and the only one in this project that chooses nothing. The manual path is
/// a dispatcher's instruction and is carried out literally: dragging two stops onto the same hour
/// produces a day that overlaps itself, which is not a route anybody could drive and which the
/// emergency-insert path therefore refuses to insert into. This is the way back — it keeps who does
/// what and in which order, and only moves the clock.
/// </para>
/// <para>
/// Shared by both schedulers for the reason <see cref="Insertion"/> is: there is nothing to search.
/// A sequence has exactly one timing, and <c>RouteTimer</c> is the one place that knows it.
/// </para>
/// </remarks>
internal static class Retiming
{
    /// <summary>Times <paramref name="order"/> as <paramref name="technician"/> would drive it.</summary>
    /// <returns>The timed run, or <see langword="null"/> if this technician cannot drive it.</returns>
    /// <exception cref="ArgumentException">
    /// The technician or one of the jobs is not in the problem. A caller that has lost track of
    /// which day it is repairing would otherwise get a plausible answer about a different one.
    /// </exception>
    public static ImmutableArray<Stop>? Of(
        SchedulingProblem problem,
        TechnicianId technician,
        IReadOnlyList<JobId> order,
        IReadOnlyDictionary<JobId, DateTimeOffset> notBefore,
        TravelMatrix distances)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(notBefore);

        var plan = problem.Technicians.FirstOrDefault(candidate => candidate.Id == technician)
            ?? throw new ArgumentException(
                $"Technician {technician.Value} is not in this problem.", nameof(technician));

        var jobs = problem.Jobs.ToDictionary(job => job.Id);
        var sequence = new List<SchedJob>(order.Count);

        foreach (var job in order)
        {
            sequence.Add(jobs.TryGetValue(job, out var found)
                ? found
                : throw new ArgumentException($"Job {job.Value} is not in this problem.", nameof(order)));
        }

        // The same arithmetic every other path is judged by. A second opinion about which days are
        // drivable is how the manual path and the engine start disagreeing.
        return RouteTimer.Time(plan, sequence, distances, notBefore);
    }
}
