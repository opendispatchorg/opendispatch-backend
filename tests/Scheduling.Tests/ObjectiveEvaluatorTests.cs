using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.Scheduling.Travel;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;
using OpenDispatch.TestSupport.Fixtures;

namespace OpenDispatch.Scheduling.Tests;

/// <summary>
/// The only judge in the engine. Everything that searches for a better day does so by asking
/// this whether one beats another, so a term that is wrong here does not produce an error —
/// it produces a schedule that is confidently, invisibly bad.
/// </summary>
/// <remarks>
/// Every drive is a flat ten minutes, so each figure below can be worked out on paper.
/// Distance is the haversine's business and is tested there. Places are kept distinct because
/// a flat rate still charges nothing for standing still.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class ObjectiveEvaluatorTests
{
    private const double DriveMinutes = 10d;

    private static readonly ConstantTravelTimeProvider Travel = new(DriveMinutes);

    private static readonly TimeWindow Shift = new(SmallCity.At(8), SmallCity.At(17));

    private static readonly GeoPoint Depot = new(51.4000d, -0.1000d);

    /// <summary>Only driving is priced, so a figure is minutes behind the wheel.</summary>
    private static readonly Objective TravelOnly = new(Travel: 1d, Lateness: 0d, Overtime: 0d, Unassigned: 0d);

    private static readonly Objective LatenessOnly = new(Travel: 0d, Lateness: 1d, Overtime: 0d, Unassigned: 0d);

    private static readonly Objective OvertimeOnly = new(Travel: 0d, Lateness: 0d, Overtime: 1d, Unassigned: 0d);

    [Fact]
    public void ADayWithNothingOnItCostsNothing()
    {
        var technician = Technician();
        var problem = SchedulingProblemBuilder.Any().Staffed(technician).Build();

        var cost = new ObjectiveEvaluator(problem, Travel).Evaluate(
            new Solution(new Dictionary<TechnicianId, ImmutableArray<Stop>>(), [], 0d));

        Assert.Equal(Cost.Zero, cost);
    }

    [Fact]
    public void ChargesForEveryDriveIncludingTheOneHome()
    {
        // Two stops is three drives: out to the first, on to the second, and back to the
        // depot. Leaving the last one out would make a route that ends on the far side of the
        // city look exactly as good as one that ends next door.
        var technician = Technician();
        var first = AllDayJobAt(1);
        var second = AllDayJobAt(2);

        var problem = SchedulingProblemBuilder.Any()
            .Weighted(TravelOnly)
            .Staffed(technician)
            .Booked(first, second)
            .Build();

        var cost = new ObjectiveEvaluator(problem, Travel).Evaluate(
            SolutionOf(technician, Worked(first, at: 9), Worked(second, at: 11)));

        Assert.Equal(3d * DriveMinutes, cost.Travel);
    }

    [Fact]
    public void ChargesLatenessOnlyForTheMinutesPastTheEndOfTheWindow()
    {
        var technician = Technician();
        var job = JobAt(1, new TimeWindow(SmallCity.At(9), SmallCity.At(10)), TimeSpan.FromMinutes(30));

        var problem = SchedulingProblemBuilder.Any()
            .Weighted(LatenessOnly)
            .Staffed(technician)
            .Booked(job)
            .Build();

        var cost = new ObjectiveEvaluator(problem, Travel).Evaluate(
            SolutionOf(technician, Worked(job, at: 10, minute: 30)));

        Assert.Equal(30d, cost.Lateness);
    }

    [Fact]
    public void AJobThatBeginsInsideItsWindowAndOverrunsIsNotLate()
    {
        // The promise was that somebody would turn up between those hours, not that the work
        // would be finished by the end of them. Measuring lateness at the far end would charge
        // for every long job booked near the close of its window.
        var technician = Technician();
        var job = JobAt(1, new TimeWindow(SmallCity.At(9), SmallCity.At(10)), TimeSpan.FromHours(3));

        var problem = SchedulingProblemBuilder.Any()
            .Weighted(LatenessOnly)
            .Staffed(technician)
            .Booked(job)
            .Build();

        var cost = new ObjectiveEvaluator(problem, Travel).Evaluate(
            SolutionOf(technician, Worked(job, at: 9, minute: 59)));

        Assert.Equal(0d, cost.Lateness);
    }

    [Fact]
    public void ChargesOvertimeForTheDriveHomeThatRunsPastTheEndOfAShift()
    {
        // Work has to finish inside the shift — that is a hard constraint — so the only way
        // to still be out afterwards is the journey home, which is exactly the overtime a
        // technician notices.
        var technician = Technician();
        var job = AllDayJobAt(1, TimeSpan.FromMinutes(30));

        var problem = SchedulingProblemBuilder.Any()
            .Weighted(OvertimeOnly)
            .Staffed(technician)
            .Booked(job)
            .Build();

        // Finishes at 16:55, five minutes before the shift ends, then drives ten minutes home.
        var cost = new ObjectiveEvaluator(problem, Travel).Evaluate(
            SolutionOf(technician, Worked(job, at: 16, minute: 25)));

        Assert.Equal(5d, cost.Overtime);
    }

    [Fact]
    public void NobodyIsOnOvertimeForGettingHomeInTime()
    {
        var technician = Technician();
        var job = AllDayJobAt(1);

        var problem = SchedulingProblemBuilder.Any()
            .Weighted(OvertimeOnly)
            .Staffed(technician)
            .Booked(job)
            .Build();

        var cost = new ObjectiveEvaluator(problem, Travel).Evaluate(SolutionOf(technician, Worked(job, at: 9)));

        Assert.Equal(0d, cost.Overtime);
    }

    [Theory]
    [InlineData(JobPriority.Low, 100d)]
    [InlineData(JobPriority.Normal, 200d)]
    [InlineData(JobPriority.High, 300d)]
    [InlineData(JobPriority.Emergency, 400d)]
    public void ChargesForUndoneWorkInProportionToHowBadlyItWasNeeded(JobPriority priority, double expected)
    {
        var dropped = SchedJobBuilder.Any().WithPriority(priority).Build();
        var problem = SchedulingProblemBuilder.Any()
            .Weighted(new Objective(Travel: 0d, Lateness: 0d, Overtime: 0d, Unassigned: 100d))
            .Booked(dropped)
            .Build();

        var cost = new ObjectiveEvaluator(problem, Travel).Evaluate(
            new Solution(new Dictionary<TechnicianId, ImmutableArray<Stop>>(), [dropped.Id], 0d));

        Assert.Equal(expected, cost.Unassigned);
    }

    [Fact]
    public void AddsUpAWholeDayTheWayItWasWorkedOutOnPaper()
    {
        // Default weights: a driving minute costs 1, a late minute 5, an overtime minute 2,
        // and a dropped job 500 for each point of its priority.
        //
        //   travel     three ten-minute drives: out, on, and home        = 30
        //   lateness   the second job starts 20 minutes past its window  = 20 x 5  = 100
        //   overtime   home by 10:30, hours to spare                     = 0
        //   dropped    one High-priority job                             = 500 x 3 = 1500
        //                                                                  total     1630
        var technician = Technician();
        var morning = new TimeWindow(SmallCity.At(8), SmallCity.At(9));

        var first = JobAt(1, morning, TimeSpan.FromHours(1));
        var second = JobAt(2, morning, TimeSpan.FromHours(1));
        var dropped = SchedJobBuilder.Any().WithPriority(JobPriority.High).Build();

        var problem = SchedulingProblemBuilder.Any()
            .Staffed(technician)
            .Booked(first, second, dropped)
            .Build();

        var cost = new ObjectiveEvaluator(problem, Travel).Evaluate(new Solution(
            new Dictionary<TechnicianId, ImmutableArray<Stop>>
            {
                [technician.Id] = [Worked(first, at: 8, minute: 10), Worked(second, at: 9, minute: 20)],
            },
            [dropped.Id],
            0d));

        Assert.Equal(new Cost(Travel: 30d, Lateness: 100d, Overtime: 0d, Unassigned: 1500d), cost);
        Assert.Equal(1630d, cost.Total);
    }

    [Fact]
    public void RefusesToPriceASolutionToSomebodyElsesProblem()
    {
        // Silently costing the wrong day is the failure mode worth spending an exception on:
        // the number would look perfectly plausible.
        var problem = SchedulingProblemBuilder.Any().Build();
        var stranger = TechPlanBuilder.Any().Build();

        Assert.Throws<ArgumentException>(() => new ObjectiveEvaluator(problem, Travel).Evaluate(
            new Solution(
                new Dictionary<TechnicianId, ImmutableArray<Stop>> { [stranger.Id] = [] },
                [],
                0d)));
    }

    [Fact]
    public void PricesTheDayTheGreedyConstructorReportsForItself()
    {
        // The constructor builds the schedule and this prices it. If the two ever disagreed,
        // every later comparison between a constructed day and an improved one would be
        // meaningless.
        var problem = SmallCity.Problem().Build();
        var haversine = new HaversineTravelTimeProvider();

        var solution = new GreedyScheduler(haversine).Solve(problem);

        Assert.Equal(solution.Cost, new ObjectiveEvaluator(problem, haversine).Evaluate(solution).Total);
    }

    private static TechPlan Technician() => TechPlanBuilder.Any().Working(Shift).BasedAt(Depot).Build();

    private static SchedJob AllDayJobAt(int place, TimeSpan? duration = null) =>
        JobAt(place, new TimeWindow(SmallCity.At(8), SmallCity.At(17)), duration ?? TimeSpan.FromHours(1));

    private static SchedJob JobAt(int place, TimeWindow window, TimeSpan duration) =>
        SchedJobBuilder.Any()
            .At(new GeoPoint(51.5000d + (place * 0.01d), -0.1000d))
            .InWindow(window)
            .Lasting(duration)
            .Build();

    /// <summary>The job, done from <paramref name="at"/>, reached after one ten-minute drive.</summary>
    private static Stop Worked(SchedJob job, int at, int minute = 0)
    {
        var start = SmallCity.At(at, minute);

        return new Stop(job.Id, start, start, start + job.Duration, DriveMinutes);
    }

    private static Solution SolutionOf(TechPlan technician, params Stop[] stops) => new(
        new Dictionary<TechnicianId, ImmutableArray<Stop>> { [technician.Id] = [.. stops] },
        [],
        0d);
}
