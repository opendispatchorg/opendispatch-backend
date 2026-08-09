using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.Scheduling.Search;
using OpenDispatch.Scheduling.Travel;

namespace OpenDispatch.Scheduling.Tests.Search;

/// <summary>
/// Puts a search state together from days written out by hand.
/// </summary>
/// <remarks>
/// Move tests need a schedule that is workable but deliberately bad — the wrong technician,
/// the wrong order — which is exactly what no constructor will ever produce. So the days are
/// stated directly and timed through <see cref="RouteTimer"/>, the same way the engine times
/// anything else.
/// </remarks>
internal static class SearchStates
{
    /// <summary>Builds a state in which each technician does the run given for them, in that order.</summary>
    public static SearchState Of(
        SchedulingProblem problem,
        ITravelTimeProvider travel,
        params (TechPlan Technician, SchedJob[] Run)[] days)
    {
        var distances = TravelMatrix.For(problem, travel);
        var routes = new Dictionary<TechnicianId, ImmutableArray<Stop>>();

        foreach (var (technician, run) in days)
        {
            routes[technician.Id] = RouteTimer.Time(technician, run, distances)
                ?? throw new ArgumentException(
                    $"{technician.Id.Value} cannot drive the day this test gave them.", nameof(days));
        }

        var scheduled = days.SelectMany(day => day.Run).Select(job => job.Id).ToHashSet();
        var unassigned = problem.Jobs.Where(job => !scheduled.Contains(job.Id)).Select(job => job.Id);

        return SearchState.From(problem, distances, new Solution(routes, unassigned, 0d));
    }
}
