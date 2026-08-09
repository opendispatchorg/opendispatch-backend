using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.Scheduling.Travel;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;
using OpenDispatch.TestSupport.Fixtures;

namespace OpenDispatch.Scheduling.Tests;

/// <summary>
/// The constructor. What it produces has to be workable rather than good — every test here is
/// about a rule that must hold, or about a job it was right to refuse.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class GreedySchedulerTests
{
    private static readonly HaversineTravelTimeProvider Travel = new();

    private static readonly GreedyScheduler Scheduler = new(Travel);

    [Fact]
    public void PlansAWholeDayWithoutBreakingAnyHardConstraint()
    {
        var problem = SmallCity.Problem().Build();

        var solution = Scheduler.Solve(problem);

        Assert.Empty(HardConstraints.Violations(problem, solution, Travel));
    }

    [Fact]
    public void GetsThroughTheWholeSmallCity()
    {
        // The fixture is solvable on purpose. If the constructor starts dropping jobs from it,
        // something has gone wrong here rather than in the city.
        var problem = SmallCity.Problem().Build();

        var solution = Scheduler.Solve(problem);

        Assert.Empty(solution.Unassigned);
    }

    [Fact]
    public void ReportsAJobNobodyIsQualifiedForRatherThanRefusingToSchedule()
    {
        var gasWork = SchedJobBuilder.Any().WithSkill("gas-safe").Build();
        var problem = SmallCity.Problem().Plus(gasWork).Build();

        var solution = Scheduler.Solve(problem);

        Assert.Contains(gasWork.Id, solution.Unassigned);
        Assert.Empty(HardConstraints.Violations(problem, solution, Travel));
    }

    [Fact]
    public void ReportsAJobTooLongForAnyShift()
    {
        // The other half of unschedulable: the skill exists, the hours do not.
        var marathon = SchedJobBuilder.Any().WithSkill("hvac").Lasting(TimeSpan.FromHours(11)).Build();
        var problem = SmallCity.Problem().Plus(marathon).Build();

        var solution = Scheduler.Solve(problem);

        Assert.Contains(marathon.Id, solution.Unassigned);
    }

    [Fact]
    public void ReportsAJobPromisedForAfterEverybodyHasGoneHome()
    {
        // The window's opening is binding, so a job promised for the evening cannot be pulled
        // forward into the shift to make it fit.
        var evening = SchedJobBuilder.Any()
            .WithSkill("hvac")
            .InWindow(new TimeWindow(SmallCity.At(20), SmallCity.At(22)))
            .Build();
        var problem = SmallCity.Problem().Plus(evening).Build();

        var solution = Scheduler.Solve(problem);

        Assert.Contains(evening.Id, solution.Unassigned);
    }

    [Fact]
    public void GivesTheSameDayEveryTimeItIsAsked()
    {
        var problem = SmallCity.Problem().Build();

        var first = Scheduler.Solve(problem);
        var second = Scheduler.Solve(problem);

        Assert.Equal(Describe(problem, first), Describe(problem, second));
    }

    [Fact]
    public void GivesTheSameDayToASecondSchedulerBuiltTheSameWay()
    {
        // Determinism has to survive a fresh instance, not just a second call on a warm one.
        var problem = SmallCity.Problem().Build();

        var first = new GreedyScheduler(new HaversineTravelTimeProvider()).Solve(problem);
        var second = new GreedyScheduler(new HaversineTravelTimeProvider()).Solve(problem);

        Assert.Equal(Describe(problem, first), Describe(problem, second));
    }

    [Fact]
    public void KeepsTheUrgentJobWhenOnlyOneOfTwoCanFit()
    {
        // Two hours of shift, three hours of work. Priority order is the only thing deciding
        // which one gets done, and it has to be the emergency.
        var shift = new TimeWindow(SmallCity.At(8), SmallCity.At(10));
        var day = new TimeWindow(SmallCity.At(8), SmallCity.At(18));

        var emergency = SchedJobBuilder.Any()
            .WithPriority(JobPriority.Emergency)
            .InWindow(day)
            .Lasting(TimeSpan.FromMinutes(90))
            .Build();
        var routine = SchedJobBuilder.Any()
            .WithPriority(JobPriority.Low)
            .InWindow(day)
            .Lasting(TimeSpan.FromMinutes(90))
            .Build();

        var problem = SchedulingProblemBuilder.Any()
            .Over(day)
            .Staffed(TechPlanBuilder.Any().Working(shift).Build())
            .Booked(routine, emergency)
            .Build();

        var solution = Scheduler.Solve(problem);

        // Assert.Single rather than comparing against a one-element collection: an
        // ImmutableArray on the actual side target-types the expected one to match, and the
        // comparison silently becomes reference equality on the two backing arrays.
        Assert.Equal(routine.Id, Assert.Single(solution.Unassigned));
    }

    [Fact]
    public void SendsAJobToTheTechnicianItIsNearest()
    {
        // Cheapest insertion, at its simplest: with two idle technicians, the job goes to the
        // one who barely has to drive.
        var day = new TimeWindow(SmallCity.At(8), SmallCity.At(18));
        var nearby = TechPlanBuilder.Any().BasedAt(new GeoPoint(51.5100d, -0.1300d)).Working(day).Build();
        var faraway = TechPlanBuilder.Any().BasedAt(new GeoPoint(51.6500d, -0.4000d)).Working(day).Build();

        var job = SchedJobBuilder.Any().At(new GeoPoint(51.5105d, -0.1305d)).InWindow(day).Build();

        var problem = SchedulingProblemBuilder.Any()
            .Over(day)
            .Staffed(faraway, nearby)
            .Booked(job)
            .Build();

        var solution = Scheduler.Solve(problem);

        Assert.Equal([job.Id], solution.RouteFor(nearby.Id).Select(stop => stop.JobId));
        Assert.Empty(solution.RouteFor(faraway.Id));
    }

    [Fact]
    public void SlotsAJobIntoTheMiddleOfARouteWhenThatIsCheaper()
    {
        // The "position" half of cheapest insertion. Two jobs are booked first — one due east
        // of the depot, one due north — and the third sits between them. Hanging it on either
        // end of the run means driving out past it and back; dropping it into the middle
        // costs barely a detour.
        var day = new TimeWindow(SmallCity.At(8), SmallCity.At(18));
        var depot = new GeoPoint(51.5000d, -0.1000d);

        var east = Somewhere(new GeoPoint(51.5000d, -0.0400d), JobPriority.High, day);
        var north = Somewhere(new GeoPoint(51.5600d, -0.1000d), JobPriority.High, day);
        var between = Somewhere(new GeoPoint(51.5300d, -0.0550d), JobPriority.Normal, day);

        var problem = SchedulingProblemBuilder.Any()
            .Over(day)
            .Staffed(TechPlanBuilder.Any().BasedAt(depot).Working(day).Build())
            .Booked(east, north, between)
            .Build();

        var run = Scheduler.Solve(problem).Routes.Values.Single().Select(stop => stop.JobId).ToList();

        // Which way round the other two end up is a coin toss the scan order settles: a round
        // trip costs the same driven either way. Where the third one goes is not.
        Assert.Equal(3, run.Count);
        Assert.Equal(between.Id, run[1]);
    }

    private static SchedJob Somewhere(GeoPoint where, JobPriority priority, TimeWindow window) =>
        SchedJobBuilder.Any()
            .At(where)
            .WithPriority(priority)
            .InWindow(window)
            .Lasting(TimeSpan.FromMinutes(30))
            .Build();

    [Fact]
    public void WaitsRatherThanStartingBeforeTheWindowOpens()
    {
        var day = new TimeWindow(SmallCity.At(8), SmallCity.At(18));
        var afternoon = SchedJobBuilder.Any().InWindow(new TimeWindow(SmallCity.At(14), SmallCity.At(16))).Build();

        var problem = SchedulingProblemBuilder.Any()
            .Over(day)
            .Staffed(TechPlanBuilder.Any().Working(day).Build())
            .Booked(afternoon)
            .Build();

        var stop = Scheduler.Solve(problem).Routes.Values.Single().Single();

        Assert.Equal(SmallCity.At(14), stop.Start);
        Assert.True(stop.Wait > TimeSpan.Zero);
    }

    [Fact]
    public void ChargesTheFirstStopOfTheDayForTheDriveFromTheHomeBase()
    {
        // A route whose first leg is free is a route that has quietly stopped paying for the
        // journey out, and every schedule would score better than it deserves.
        var day = new TimeWindow(SmallCity.At(8), SmallCity.At(18));
        var technician = TechPlanBuilder.Any().BasedAt(new GeoPoint(51.5074d, -0.1278d)).Working(day).Build();
        var job = SchedJobBuilder.Any().At(new GeoPoint(51.5390d, -0.1426d)).InWindow(day).Build();

        var problem = SchedulingProblemBuilder.Any()
            .Over(day)
            .Staffed(technician)
            .Booked(job)
            .Build();

        var stop = Scheduler.Solve(problem).RouteFor(technician.Id).Single();

        Assert.Equal(Travel.Minutes(technician.HomeBase, job.Location), stop.TravelMin);
        Assert.Equal(day.Start + TimeSpan.FromMinutes(stop.TravelMin), stop.Arrival);
    }

    [Fact]
    public void LeavesEveryJobUnassignedWhenThereIsNobodyToDoThem()
    {
        var problem = SchedulingProblemBuilder.Any().Booked(SchedJobBuilder.Any().Build()).Build();

        var solution = Scheduler.Solve(problem);

        Assert.Single(solution.Unassigned);
        Assert.Empty(solution.Routes);
    }

    [Fact]
    public void AnEmptyDayIsAnEmptySchedule()
    {
        var solution = Scheduler.Solve(SchedulingProblemBuilder.Any().Build());

        Assert.Empty(solution.Unassigned);
        Assert.Equal(0d, solution.Cost);
    }

    [Fact]
    public void GivesEveryTechnicianADayEvenIfItIsAnEmptyOne()
    {
        // So the caller writing assignments, and the board drawing the timeline, get a row per
        // technician without having to reconcile the solution against the problem.
        var problem = SmallCity.Problem().Booked().Build();

        var solution = Scheduler.Solve(problem);

        Assert.Equal(problem.Technicians.Length, solution.Routes.Count);
    }

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
