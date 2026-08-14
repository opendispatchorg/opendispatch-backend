using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.Scheduling.Travel;

namespace OpenDispatch.Scheduling;

/// <summary>
/// Finds the best place in a day that has already been planned for one more job.
/// </summary>
/// <remarks>
/// <para>
/// The emergency-dispatch operation. A boiler has failed, the day is already running, and the
/// dispatcher needs to know where this fits — not a fresh plan for the afternoon. Every stop
/// that is already scheduled keeps the technician it was given and its place in their run; the
/// only thing that moves is the clock, for the stops after the one being slotted in, because a
/// job inserted at eleven pushes the rest of that technician's afternoon along.
/// </para>
/// <para>
/// Every position on every technician is tried and priced with the full objective — the same
/// standard <see cref="GreedyScheduler"/> uses, so the day a dispatcher gets from re-optimising
/// and the slot they get from dropping in one emergency are chosen the same way. Mileage alone
/// would put an emergency at the end of somebody's day, because the technician passes the door
/// on the way home, which for a two-hour window is the wrong answer expensively.
/// </para>
/// <para>
/// Shared by both schedulers because there is nothing to anneal about it. A single insertion
/// that may not disturb anything else has one right answer, and this searches all of them.
/// </para>
/// </remarks>
internal static class Insertion
{
    /// <summary>
    /// Slots <paramref name="job"/> into the cheapest place it will go, or reports it
    /// unassigned if it will not go anywhere.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The job is not in the problem, the job is already on somebody's day, or the schedule
    /// answers a different problem.
    /// </exception>
    public static Solution Into(
        Solution current,
        SchedulingProblem problem,
        JobId job,
        TravelMatrix distances)
    {
        var jobs = problem.Jobs.ToDictionary(scheduled => scheduled.Id);
        var technicians = problem.Technicians.ToDictionary(technician => technician.Id);

        if (!jobs.TryGetValue(job, out var arriving))
        {
            throw new ArgumentException($"Job {job.Value} is not in this problem.", nameof(job));
        }

        foreach (var technician in current.Routes.Keys)
        {
            if (!technicians.ContainsKey(technician))
            {
                throw new ArgumentException(
                    $"The schedule gives work to {technician.Value}, who is not in this problem.", nameof(current));
            }
        }

        var runs = problem.Technicians.ToDictionary(
            technician => technician.Id,
            technician => current.RouteFor(technician.Id).Select(stop => jobs[stop.JobId]).ToList());

        if (runs.Any(run => run.Value.Any(scheduled => scheduled.Id == job)))
        {
            throw new ArgumentException(
                $"Job {job.Value} is already on somebody's day; there is nothing to slot in.", nameof(job));
        }

        var objective = new ObjectiveEvaluator(problem, distances);
        var placement = Cheapest(arriving, problem, runs, current, objective, distances, Promised(current));
        var routes = problem.Technicians.ToDictionary(
            technician => technician.Id,
            technician => current.RouteFor(technician.Id));

        ImmutableArray<JobId> unassigned;

        if (placement is null)
        {
            unassigned = current.Unassigned.Contains(job) ? current.Unassigned : current.Unassigned.Add(job);
        }
        else
        {
            routes[placement.Technician] = placement.Stops;
            unassigned = [.. current.Unassigned.Where(dropped => dropped != job)];
        }

        return new Solution(routes, unassigned, objective.Evaluate(routes, unassigned).Total);
    }

    /// <summary>
    /// The technician and position that the job costs least to add to, or nothing if it will
    /// not fit anywhere.
    /// </summary>
    /// <remarks>
    /// Costs are compared as the difference one route makes, which is the same ordering as
    /// comparing whole schedules: every other route is untouched, and the penalty for leaving
    /// the job undone is the same figure whichever placement wins.
    /// </remarks>
    private static Placement? Cheapest(
        SchedJob arriving,
        SchedulingProblem problem,
        Dictionary<TechnicianId, List<SchedJob>> runs,
        Solution current,
        ObjectiveEvaluator objective,
        TravelMatrix distances,
        IReadOnlyDictionary<JobId, DateTimeOffset> promised)
    {
        Placement? cheapest = null;
        var lowestExtra = double.PositiveInfinity;

        foreach (var technician in problem.Technicians)
        {
            var run = runs[technician.Id];
            var today = objective.EvaluateRoute(technician, current.RouteFor(technician.Id)).Total;

            for (var position = 0; position <= run.Count; position++)
            {
                var candidate = new List<SchedJob>(run);
                candidate.Insert(position, arriving);

                if (RouteTimer.Time(technician, candidate, distances, promised) is not { } stops)
                {
                    continue;
                }

                // Strictly less, so the first technician and the earliest position to reach a
                // given cost keep it. That is what makes a tie deterministic.
                var extra = objective.EvaluateRoute(technician, stops).Total - today;
                if (extra < lowestExtra)
                {
                    lowestExtra = extra;
                    cheapest = new Placement(technician.Id, stops);
                }
            }
        }

        return cheapest;
    }

    /// <summary>
    /// When each stop already on the day was promised for — the times the re-timing may not
    /// pull earlier.
    /// </summary>
    /// <remarks>
    /// Without this, timing a candidate run rebuilds every stop's clock from the start of the
    /// shift, so slotting an emergency into the morning drags a two o'clock appointment back to
    /// half past nine — including one a technician is already driving to. A dispatcher asking
    /// where one job fits must not be answered with a different afternoon.
    /// </remarks>
    private static Dictionary<JobId, DateTimeOffset> Promised(Solution current)
    {
        var promised = new Dictionary<JobId, DateTimeOffset>();

        foreach (var route in current.Routes.Values)
        {
            foreach (var stop in route)
            {
                promised[stop.JobId] = stop.Start;
            }
        }

        return promised;
    }

    /// <summary>Whose day the job joins, and that day as it ends up.</summary>
    private sealed record Placement(TechnicianId Technician, ImmutableArray<Stop> Stops);
}
