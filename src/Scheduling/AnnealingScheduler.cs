using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.Scheduling.Search;
using OpenDispatch.Scheduling.Travel;

namespace OpenDispatch.Scheduling;

/// <summary>
/// Builds a day with the greedy constructor and then spends a while trying to make it better,
/// accepting the occasional worse schedule on the way so it does not get stuck.
/// </summary>
/// <remarks>
/// <para>
/// The engine proper. Cheapest insertion gets to a workable day quickly and then cannot
/// improve on it — every rearrangement worth making passes through an arrangement that is
/// worse, and a constructor that only ever accepts an improvement can never take that step.
/// Annealing takes it: early on it will accept almost anything, and as the temperature falls
/// it becomes steadily fussier until it is only accepting improvements at all.
/// </para>
/// <para>
/// Reproducible, because a demo that produces a different day each time it is run is not a
/// demo and a bug that cannot be replayed cannot be fixed. Everything random comes from one
/// generator seeded out of <see cref="SchedulingProblem.Seed"/>, and every choice that is not
/// random is made in the problem's own order. The same problem gives the same day.
/// </para>
/// <para>
/// On the same machine, that is. <c>Random(seed)</c> is stable across platforms and .NET
/// versions, but deciding whether to accept a worse day goes through <see cref="Math.Exp"/>,
/// which is not guaranteed bit-identical everywhere — so two different machines could in
/// principle part company. Nothing depends on them not doing so today; if something ever
/// needs to, the fix is a rational approximation to the acceptance rule rather than a
/// cross-platform maths library.
/// </para>
/// <para>
/// What it returns is the best schedule it saw, not the one it happened to be holding when the
/// iterations ran out — those are not the same thing, and the difference is the whole point of
/// being willing to accept a worse day in the middle.
/// </para>
/// <para>
/// It cannot rescue an unassigned job. None of the moves reaches the unassigned pile, so
/// whatever the constructor could not place stays unplaced however much room the search frees
/// up elsewhere.
/// </para>
/// <para>
/// The cooling, measured, does not earn its place. Against the same search configured never to
/// accept a worse day, this one wins on four fixtures, loses on three and ties on one, every
/// gap inside ±4%: the local search is doing the work and the temperature is along for the
/// ride. Three angles were tried and none changed that — recalibrating the temperature from the
/// measured distribution of uphill moves, starting the search from a better day, and improving
/// the proposals it draws. So retuning is not the answer, and nobody should turn these knobs
/// expecting gains. It stays because Document 2 §4 names simulated annealing, and because what
/// it produces is feasible, reproducible and far cheaper than the constructor's day either way.
/// </para>
/// <para>
/// The one lead left untried, for whoever picks this up: on a densely booked day most proposals
/// are refused because the day is already full of driving and waiting rather than because the
/// work itself does not fit. Pre-empting those would need the route timed, which is the
/// expensive thing the check exists to avoid, so a cheaper approximation would narrow the
/// neighbourhood in ways that are hard to reason about. Worth doing carefully or not at all.
/// </para>
/// </remarks>
public sealed class AnnealingScheduler : IScheduler
{
    private readonly ITravelTimeProvider _travel;
    private readonly AnnealingOptions _options;

    /// <summary>Creates a scheduler that searches with the default settings.</summary>
    public AnnealingScheduler(ITravelTimeProvider travel)
        : this(travel, AnnealingOptions.Default)
    {
    }

    /// <summary>Creates a scheduler that searches as <paramref name="options"/> says.</summary>
    public AnnealingScheduler(ITravelTimeProvider travel, AnnealingOptions options)
    {
        ArgumentNullException.ThrowIfNull(travel);
        ArgumentNullException.ThrowIfNull(options);

        _travel = travel;
        _options = options;
    }

    /// <inheritdoc />
    public Solution Solve(SchedulingProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);

        // Worked out once and handed to both halves: the constructor and the search ask about
        // the same drives, and a road-network provider should be asked once.
        var distances = TravelMatrix.For(problem, _travel);
        var state = SearchState.From(problem, distances, GreedyScheduler.Solve(problem, distances));

        var random = new Random(problem.Seed);
        var generator = new MoveGenerator(problem, random);

        var best = state.ToSolution();
        var bestCost = state.Cost.Total;
        var temperature = _options.StartTemperature;

        for (var iteration = 0; iteration < _options.Iterations; iteration++)
        {
            if (generator.Propose(state) is { } move &&
                state.Try(move) is { } outcome &&
                Accepts(outcome.Cost.Total - state.Cost.Total, temperature, random))
            {
                state.Commit(outcome);

                if (state.Cost.Total < bestCost)
                {
                    bestCost = state.Cost.Total;
                    best = state.ToSolution();
                }
            }

            temperature *= _options.Cooling;
        }

        return best;
    }

    /// <inheritdoc />
    /// <remarks>
    /// There is nothing to anneal about slotting in one job. Searching would mean rearranging
    /// the day, which is exactly what this is not allowed to do, so it does the same exhaustive
    /// thing the constructor does.
    /// </remarks>
    public Solution Insert(Solution current, SchedulingProblem problem, JobId job)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(problem);

        return Insertion.Into(current, problem, job, TravelMatrix.For(problem, _travel));
    }

    /// <inheritdoc />
    public ImmutableArray<Stop>? Retime(
        SchedulingProblem problem,
        TechnicianId technician,
        IReadOnlyList<JobId> order,
        IReadOnlyDictionary<JobId, DateTimeOffset> notBefore)
    {
        ArgumentNullException.ThrowIfNull(problem);

        return Retiming.Of(problem, technician, order, notBefore, TravelMatrix.For(problem, _travel));
    }

    /// <summary>
    /// Whether to take a rearrangement that costs <paramref name="extra"/> more than the day
    /// in hand.
    /// </summary>
    /// <remarks>
    /// The Metropolis rule. An improvement is always taken. A worse day is taken with a
    /// probability that falls away as it gets worse and as the temperature drops, so early on
    /// the search roams and by the end it is only climbing. A temperature that has cooled to
    /// nothing makes the exponent negative infinity, which is zero probability rather than an
    /// error — hill climbing, which is the right way for this to end.
    /// </remarks>
    private static bool Accepts(double extra, double temperature, Random random) =>
        extra <= 0d || random.NextDouble() < Math.Exp(-extra / temperature);
}
