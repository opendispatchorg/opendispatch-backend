using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.Scheduling.Travel;

namespace OpenDispatch.Scheduling;

/// <summary>
/// Builds a workable day by cheapest insertion: take the jobs in turn, most urgent first, and
/// slot each one wherever it adds the least driving.
/// </summary>
/// <remarks>
/// <para>
/// This is the constructor, not the optimiser. It never reconsiders a placement — once a job
/// is on someone's route it stays there — so what comes out is a feasible day rather than a
/// good one. Improving it is the local search's job, and having a fast, deterministic,
/// obviously-correct starting point is what lets the search be judged.
/// </para>
/// <para>
/// The hard constraints are the ones the architecture names, and each is enforced by
/// construction rather than checked afterwards:
/// </para>
/// <list type="bullet">
///   <item>a technician is only offered work they hold the skill for;</item>
///   <item>every stop starts and finishes inside their shift, or the placement is refused;</item>
///   <item>a stop cannot begin until the technician has driven there from the last one, so a
///     route can never overlap itself.</item>
/// </list>
/// <para>
/// The promised window is not a constraint here, with one exception: a technician who arrives
/// before the window opens waits, because turning up early is not the same as being allowed
/// to start early. The far end of the window is soft — running past it is legal and costs
/// something, which is the whole reason a job is almost never unschedulable for want of time.
/// A job comes back unassigned because no one holds the skill, or because no shift can
/// contain it.
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

        var distances = TravelMatrix.For(problem, _travel);
        var sequences = problem.Technicians.ToDictionary(technician => technician.Id, _ => new List<SchedJob>());
        var plans = problem.Technicians.ToDictionary(technician => technician.Id, _ => RoutePlan.Empty);
        var unassigned = new List<JobId>();

        foreach (var job in InInsertionOrder(problem))
        {
            var placement = CheapestPlacement(job, problem, sequences, plans, distances);

            if (placement is null)
            {
                unassigned.Add(job.Id);
                continue;
            }

            sequences[placement.Technician].Insert(placement.Position, job);
            plans[placement.Technician] = placement.Plan;
        }

        // Summed over the technicians in the problem's own order rather than over the
        // dictionary: adding doubles is not associative, so an iteration order the runtime
        // chooses would be an iteration order that could change the answer.
        var travelled = problem.Technicians.Sum(technician => plans[technician.Id].TravelMinutes);

        return new Solution(
            problem.Technicians.ToDictionary(technician => technician.Id, technician => plans[technician.Id].Stops),
            unassigned,

            // TEMPORARY: removed in step 16. The greedy can only price the driving it can
            // see; lateness, overtime and the cost of a dropped job arrive with the objective
            // evaluator, and Solve reports that number instead.
            problem.Weights.Travel * travelled);
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
    /// The technician and position that add the least driving, or nothing if no technician can
    /// take the job at all.
    /// </summary>
    private static Placement? CheapestPlacement(
        SchedJob job,
        SchedulingProblem problem,
        Dictionary<TechnicianId, List<SchedJob>> sequences,
        Dictionary<TechnicianId, RoutePlan> plans,
        TravelMatrix distances)
    {
        Placement? cheapest = null;
        var lowestExtraMinutes = double.PositiveInfinity;

        foreach (var technician in problem.Technicians)
        {
            if (!technician.HasSkill(job.RequiredSkill))
            {
                continue;
            }

            var sequence = sequences[technician.Id];
            var travelledSoFar = plans[technician.Id].TravelMinutes;

            for (var position = 0; position <= sequence.Count; position++)
            {
                var candidate = new List<SchedJob>(sequence);
                candidate.Insert(position, job);

                var plan = Plan(technician, candidate, distances);
                if (plan is null)
                {
                    continue;
                }

                // Strictly less, so the first technician and the earliest position to reach a
                // given cost keep it. That is what makes a tie deterministic.
                var extraMinutes = plan.TravelMinutes - travelledSoFar;
                if (extraMinutes < lowestExtraMinutes)
                {
                    lowestExtraMinutes = extraMinutes;
                    cheapest = new Placement(technician.Id, position, plan);
                }
            }
        }

        return cheapest;
    }

    /// <summary>
    /// Drives the route in order and works out when the technician is where, or refuses it if
    /// the day does not fit inside the shift.
    /// </summary>
    private static RoutePlan? Plan(TechPlan technician, List<SchedJob> sequence, TravelMatrix distances)
    {
        var stops = ImmutableArray.CreateBuilder<Stop>(sequence.Count);
        var clock = technician.Shift.Start;
        var travelMinutes = 0d;
        SchedJob? previous = null;

        foreach (var job in sequence)
        {
            var leg = previous is null
                ? distances.FromHomeBase(technician.Id, job.Id)
                : distances.BetweenJobs(previous.Id, job.Id);

            var arrival = clock + TimeSpan.FromMinutes(leg);

            // Early is not the same as allowed. The window's opening is the one part of the
            // customer's promise the schedule treats as binding; its closing is not.
            var start = arrival > job.Window.Start ? arrival : job.Window.Start;
            var end = start + job.Duration;

            if (end > technician.Shift.End)
            {
                return null;
            }

            stops.Add(new Stop(job.Id, arrival, start, end, leg));
            travelMinutes += leg;
            clock = end;
            previous = job;
        }

        return new RoutePlan(stops.DrainToImmutable(), travelMinutes);
    }

    /// <summary>A technician's route, timed, with the driving it costs.</summary>
    private sealed record RoutePlan(ImmutableArray<Stop> Stops, double TravelMinutes)
    {
        public static RoutePlan Empty { get; } = new([], 0d);
    }

    /// <summary>Where a job would go, and the route that results.</summary>
    private sealed record Placement(TechnicianId Technician, int Position, RoutePlan Plan);
}
