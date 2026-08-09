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
/// What it returns is the best schedule it saw, not the one it happened to be holding when the
/// iterations ran out — those are not the same thing, and the difference is the whole point of
/// being willing to accept a worse day in the middle.
/// </para>
/// <para>
/// It cannot rescue an unassigned job. None of the moves reaches the unassigned pile, so
/// whatever the constructor could not place stays unplaced however much room the search frees
/// up elsewhere.
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
