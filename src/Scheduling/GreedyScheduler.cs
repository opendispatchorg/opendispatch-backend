using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.Scheduling.Travel;

namespace OpenDispatch.Scheduling;

/// <summary>
/// Builds a workable day by cheapest insertion: take the jobs in turn, most urgent first, and
/// slot each one wherever it adds least to the cost of the day.
/// </summary>
/// <remarks>
/// <para>
/// This is the constructor, not the optimiser. It never reconsiders a placement — once a job
/// is on someone's route it stays there — so what comes out is a workable day rather than the
/// best one. Improving it is the local search's job, and having a fast, deterministic,
/// obviously-correct starting point is what lets the search be judged.
/// </para>
/// <para>
/// Candidates are priced with the whole objective, not by driving alone. Mileage is the
/// cheaper thing to measure and the obvious thing for a constructor to use, and it was what
/// this did first — but it produces a day that is short on driving and hours late, because
/// nothing in the criterion knows what a promised window is worth. Pricing the whole objective
/// costs nothing measurable: the expensive part of considering a position is timing the route,
/// which has to happen either way.
/// </para>
/// <para>
/// It is also the same rule <see cref="Insertion"/> uses, so the day a dispatcher gets from
/// re-optimising and the slot they get from dropping in one emergency are chosen by the same
/// standard.
/// </para>
/// <para>
/// Which placements are allowed at all is <see cref="RouteTimer"/>'s to say — skill, shift and
/// the drive between consecutive stops. A job comes back unassigned because no technician
/// holds the skill, or because no shift can contain it.
/// </para>
/// <para>
/// Deterministic without needing the problem's seed: there is no randomness to seed. The same
/// problem gives the same day, on any machine, because ties are settled by the order the
/// technicians and jobs arrive in rather than by anything the runtime chooses.
/// </para>
/// </remarks>
public sealed class GreedyScheduler : IScheduler
{
    private readonly ITravelTimeProvider _travel;

    /// <summary>Creates a scheduler that measures drives with <paramref name="travel"/>.</summary>
    public GreedyScheduler(ITravelTimeProvider travel)
    {
        ArgumentNullException.ThrowIfNull(travel);

        _travel = travel;
    }

    /// <inheritdoc />
    public Solution Solve(SchedulingProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        return Solve(problem, TravelMatrix.For(problem, _travel));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Nothing greedy about this one. A single insertion that may not disturb anything else
    /// has one right answer, so both schedulers do the same exhaustive thing.
    /// </remarks>
    public Solution Insert(Solution current, SchedulingProblem problem, JobId job)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(problem);

        return Insertion.Into(current, problem, job, TravelMatrix.For(problem, _travel));
    }

    /// <summary>
    /// Builds the day against drives somebody has already worked out — for a search that goes
    /// on to improve it and would otherwise ask the provider for the same matrix twice. With a
    /// road-network provider that is a second batched request for an answer already in hand.
    /// </summary>
    internal static Solution Solve(SchedulingProblem problem, TravelMatrix distances)
    {
        var objective = new ObjectiveEvaluator(problem, distances);
        var runs = problem.Technicians.ToDictionary(technician => technician.Id, _ => new List<SchedJob>());
        var routes = problem.Technicians.ToDictionary(technician => technician.Id, _ => ImmutableArray<Stop>.Empty);
        var unassigned = new List<JobId>();

        foreach (var job in InInsertionOrder(problem))
        {
            var placement = CheapestPlacement(job, problem, runs, routes, objective, distances);

            if (placement is null)
            {
                unassigned.Add(job.Id);
                continue;
            }

            runs[placement.Technician].Insert(placement.Position, job);
            routes[placement.Technician] = placement.Stops;
        }

        return new Solution(routes, unassigned, objective.Evaluate(routes, unassigned).Total);
    }

    /// <summary>
    /// Most urgent first, and among equally urgent jobs the one promised earliest.
    /// </summary>
    /// <remarks>
    /// Insertion order is the whole of a greedy heuristic's judgement — a job placed early
    /// gets the pick of the day, and everything after it works around that. Urgency first is
    /// what the architecture asks for. The promised time breaks ties because a job booked for
    /// eight o'clock, inserted after one booked for four, may find its own morning already
    /// taken. Remaining ties keep the order the problem stated, so the sort settles them the
    /// same way every run.
    /// </remarks>
    private static IEnumerable<SchedJob> InInsertionOrder(SchedulingProblem problem) =>
        problem.Jobs
            .OrderByDescending(job => job.Priority)
            .ThenBy(job => job.Window.Start);

    /// <summary>
    /// The technician and position that add least to the cost of the day, or nothing if nobody
    /// can take the job at all.
    /// </summary>
    /// <remarks>
    /// Compared as the difference one route makes, which orders candidates the same way as
    /// comparing whole schedules would: every other route is untouched, and the penalty for
    /// leaving the job undone is the same figure whichever placement wins.
    /// </remarks>
    private static Placement? CheapestPlacement(
        SchedJob job,
        SchedulingProblem problem,
        Dictionary<TechnicianId, List<SchedJob>> runs,
        Dictionary<TechnicianId, ImmutableArray<Stop>> routes,
        ObjectiveEvaluator objective,
        TravelMatrix distances)
    {
        Placement? cheapest = null;
        var lowestExtra = double.PositiveInfinity;

        foreach (var technician in problem.Technicians)
        {
            var run = runs[technician.Id];
            var today = objective.EvaluateRoute(technician, routes[technician.Id]).Total;

            for (var position = 0; position <= run.Count; position++)
            {
                var candidate = new List<SchedJob>(run);
                candidate.Insert(position, job);

                if (RouteTimer.Time(technician, candidate, distances) is not { } stops)
                {
                    continue;
                }

                // Strictly less, so the first technician and the earliest position to reach a
                // given cost keep it. That is what makes a tie deterministic.
                var extra = objective.EvaluateRoute(technician, stops).Total - today;
                if (extra < lowestExtra)
                {
                    lowestExtra = extra;
                    cheapest = new Placement(technician.Id, position, stops);
                }
            }
        }

        return cheapest;
    }

    /// <summary>Where a job would go, and the day that results.</summary>
    private sealed record Placement(TechnicianId Technician, int Position, ImmutableArray<Stop> Stops);
}
