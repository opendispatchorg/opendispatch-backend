using OpenDispatch.Scheduling.Model;
using OpenDispatch.Scheduling.Travel;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;
using OpenDispatch.TestSupport.Fixtures;

namespace OpenDispatch.Scheduling.Tests;

/// <summary>
/// The engine proper. Two things have to hold whatever else changes: it produces the same day
/// every time it is given the same problem, and the day it produces is better than the one the
/// constructor handed it.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class AnnealingSchedulerTests
{
    private static readonly HaversineTravelTimeProvider Travel = new();

    /// <summary>Enough iterations to exercise the loop without spending a second on every test.</summary>
    private static readonly AnnealingOptions Brief = AnnealingOptions.Default with { Iterations = 2_000 };

    /// <summary>
    /// Both shapes the engine is judged on: the hand-written city small enough to check by
    /// eye, and the generated day big enough that being nearly right by accident is not on.
    /// </summary>
    public static TheoryData<string> Fixtures => new() { "small city", "busy day" };

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void GivesTheSameDayEveryRunForAGivenSeed(string fixture)
    {
        // A demo that produces a different schedule each time is not a demo, and a bug that
        // cannot be replayed cannot be fixed.
        var problem = Problem(fixture);

        var first = new AnnealingScheduler(Travel).Solve(problem);
        var second = new AnnealingScheduler(Travel).Solve(problem);

        Assert.Equal(Describe(problem, first), Describe(problem, second));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void BeatsTheDayTheConstructorHandedIt(string fixture)
    {
        // Measured with the default settings, across both fixtures and three layout seeds:
        //
        //   small city    3 techs,   8 jobs      383 ->    50     87% cheaper    ~36 ms
        //   busy day      5 techs,  30 jobs    7,258 ->   224     97% cheaper    ~27 ms
        //   bigger day    8 techs,  60 jobs   12,229 -> 4,357     64% cheaper    ~30 ms
        //
        // Almost all of it is lateness. The constructor picks by driving alone and produces a
        // day that is workable but hours late; the search trades a little more driving for
        // keeping the promises. A quarter is asserted rather than the measured figure, so this
        // reports a regression in the engine rather than a rounding change in a fixture.
        var problem = Problem(fixture);

        var constructed = new GreedyScheduler(Travel).Solve(problem);
        var annealed = new AnnealingScheduler(Travel).Solve(problem);

        Assert.True(
            annealed.Cost < constructed.Cost * 0.75d,
            $"annealing got {annealed.Cost:F1} from {constructed.Cost:F1}, which is not a meaningful improvement");
    }

    [Fact]
    public void BeatsASearchThatTakesWhateverItIsOffered()
    {
        // What the acceptance rule is worth. Set the temperature high enough that nothing ever
        // cools and every rearrangement is taken, and the search becomes a random walk: on the
        // busy day it lands around 1,100 where the real schedule costs 224. Without this, a
        // scheduler that accepted everything would pass every other test here, because keeping
        // the best day it saw would carry it.
        var problem = BusyDay.Problem().Build();
        var aimless = AnnealingOptions.Default with { StartTemperature = 1e12d, Cooling = 0.9999999d };

        var annealed = new AnnealingScheduler(Travel).Solve(problem);
        var wandered = new AnnealingScheduler(Travel, aimless).Solve(problem);

        Assert.True(
            annealed.Cost < wandered.Cost * 0.75d,
            $"cooling bought nothing: {annealed.Cost:F1} against {wandered.Cost:F1} for a random walk");
    }

    [Fact]
    public void SearchingForLongerOnTheSameSeedIsNeverAWorseAnswer()
    {
        // The property that only holds because the search reports the best day it saw rather
        // than the one it was holding when the iterations ran out. Same seed and same starting
        // day, so the long run's first five thousand steps *are* the short run's entire life —
        // which makes more searching monotone. Return the day in hand instead and the answer
        // is wherever the walk happened to stop, where more of it is as likely to be worse.
        //
        // Deliberately kept hot so it never settles: under the default cooling the search is
        // only climbing by the end, and the last day it holds is the best one anyway.
        var problem = BusyDay.Problem().Build();
        var hot = AnnealingOptions.Default with { StartTemperature = 200d, Cooling = 0.99999999d };

        var brief = new AnnealingScheduler(Travel, hot with { Iterations = 5_000 }).Solve(problem);
        var patient = new AnnealingScheduler(Travel, hot with { Iterations = 50_000 }).Solve(problem);

        Assert.True(
            patient.Cost <= brief.Cost,
            $"ten times the searching came back with {patient.Cost:F1} against {brief.Cost:F1}");
    }

    [Fact]
    public void TradesDrivingForKeepingPromises()
    {
        // What the improvement actually consists of, stated outright — the objective says a
        // late minute is worth five driving ones, and this is the engine acting on it.
        var problem = SmallCity.Problem().Build();
        var objective = new ObjectiveEvaluator(problem, Travel);

        var constructed = objective.Evaluate(new GreedyScheduler(Travel).Solve(problem));
        var annealed = objective.Evaluate(new AnnealingScheduler(Travel).Solve(problem));

        Assert.True(constructed.Lateness > 0d, "the constructor's day was already on time, so there was nothing to fix");
        Assert.True(annealed.Lateness < constructed.Lateness);
        Assert.True(annealed.Total < constructed.Total);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void EverythingItProducesIsStillADaySomebodyCouldDrive(string fixture)
    {
        var problem = Problem(fixture);

        var solution = new AnnealingScheduler(Travel).Solve(problem);

        Assert.Empty(HardConstraints.Violations(problem, solution, Travel));
    }

    [Fact]
    public void LeavesExactlyTheJobsTheConstructorCouldNotPlace()
    {
        // None of the moves reaches the unassigned pile, so the search cannot rescue a dropped
        // job however much room it frees up. Worth pinning: it is the clearest limit on what
        // the engine can currently do.
        var problem = SmallCity.Problem()
            .Plus(SchedJobBuilder.Any().WithSkill("gas-safe").Build())
            .Build();

        var constructed = new GreedyScheduler(Travel).Solve(problem);
        var annealed = new AnnealingScheduler(Travel).Solve(problem);

        Assert.NotEmpty(constructed.Unassigned);
        Assert.Equal(constructed.Unassigned.Order(), annealed.Unassigned.Order());
    }

    [Fact]
    public void WithNoIterationsItIsJustTheConstructorsDay()
    {
        var problem = SmallCity.Problem().Build();

        var constructed = new GreedyScheduler(Travel).Solve(problem);
        var annealed = new AnnealingScheduler(Travel, AnnealingOptions.Default with { Iterations = 0 })
            .Solve(problem);

        Assert.Equal(Describe(problem, constructed), Describe(problem, annealed));
    }

    [Fact]
    public void ReportsTheRealCostOfWhatItReturns()
    {
        // The search reports the best day it saw, which is not the one it was holding at the
        // end. If the cost travelled separately from the schedule, it would be the cost of a
        // day nobody is looking at.
        var problem = BusyDay.Problem().Build();

        var solution = new AnnealingScheduler(Travel, Brief).Solve(problem);

        Assert.Equal(solution.Cost, new ObjectiveEvaluator(problem, Travel).Evaluate(solution).Total, 6);
    }

    [Fact]
    public void AProblemWithNothingInItIsStillAnEmptyDay()
    {
        var solution = new AnnealingScheduler(Travel, Brief).Solve(SchedulingProblemBuilder.Any().Build());

        Assert.Empty(solution.Unassigned);
        Assert.Equal(0d, solution.Cost);
    }

    [Fact]
    public void ADayNobodyCanWorkIsNotSomethingToSearch()
    {
        // Every job unassignable, nobody with anything to do. The move generator has no stop
        // to draw and must not fall over looking for one.
        var problem = SchedulingProblemBuilder.Any()
            .Staffed(TechPlanBuilder.Any().Skilled("hvac").Build())
            .Booked(SchedJobBuilder.Any().WithSkill("gas-safe").Build())
            .Build();

        var solution = new AnnealingScheduler(Travel, Brief).Solve(problem);

        Assert.Single(solution.Unassigned);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    public void RefusesAStartingTemperatureThatIsNotOne(double temperature)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AnnealingOptions.Default with { StartTemperature = temperature });
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(1d)]
    [InlineData(1.5d)]
    public void RefusesACoolingRateThatWouldNeverSettleOrSettleAtOnce(double cooling)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AnnealingOptions.Default with { Cooling = cooling });
    }

    [Fact]
    public void RefusesANegativeNumberOfIterations()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AnnealingOptions.Default with { Iterations = -1 });
    }

    private static SchedulingProblem Problem(string fixture) => fixture switch
    {
        "small city" => SmallCity.Problem().Build(),
        "busy day" => BusyDay.Problem().Build(),
        _ => throw new ArgumentOutOfRangeException(nameof(fixture), fixture, "No such fixture."),
    };

    /// <summary>
    /// A schedule as text, in the problem's own order, for comparing two runs. Reads as the
    /// day it describes when a determinism test fails.
    /// </summary>
    private static string Describe(SchedulingProblem problem, Solution solution) =>
        string.Join(
            "\n",
            problem.Technicians
                .Select(technician => $"{technician.Id.Value}: " + string.Join(
                    " -> ",
                    solution.RouteFor(technician.Id).Select(stop => $"{stop.JobId.Value}@{stop.Start:O}/{stop.TravelMin:R}")))
                .Append($"unassigned: {string.Join(", ", solution.Unassigned.Select(job => job.Value))}")
                .Append($"cost: {solution.Cost:R}"));
}
