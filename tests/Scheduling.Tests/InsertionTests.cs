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
/// Emergency dispatch. The day is already running, so the only acceptable answer is one that
/// tells a dispatcher where this job goes without rewriting the afternoon somebody is already
/// driving.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class InsertionTests
{
    private static readonly HaversineTravelTimeProvider Travel = new();

    private static readonly AnnealingScheduler Scheduler = new(Travel);

    [Fact]
    public void PutsTheJobOnSomebodysDay()
    {
        var (problem, planned, rush) = ADayWithSomethingElseComingIn();

        var revised = Scheduler.Insert(planned, problem, rush.Id);

        Assert.DoesNotContain(rush.Id, revised.Unassigned);
        Assert.Contains(rush.Id, revised.Routes.Values.SelectMany(route => route).Select(stop => stop.JobId));
        Assert.Empty(HardConstraints.Violations(problem, revised, Travel));
    }

    [Fact]
    public void LeavesEveryStopItDidNotHaveToMoveExactlyWhereItWas()
    {
        // The whole point. A dispatcher who asks where an emergency fits must not get back a
        // day in which three technicians have been sent somewhere else.
        var (problem, planned, rush) = ADayWithSomethingElseComingIn();

        var revised = Scheduler.Insert(planned, problem, rush.Id);

        var receiving = WhoTook(revised, rush.Id);

        foreach (var technician in problem.Technicians.Where(technician => technician.Id != receiving))
        {
            Assert.Equal(
                planned.RouteFor(technician.Id).AsEnumerable(),
                revised.RouteFor(technician.Id).AsEnumerable());
        }
    }

    [Fact]
    public void KeepsTheReceivingTechniciansOtherStopsInTheOrderTheyWereIn()
    {
        // Their clock moves — a job slotted in at eleven pushes the rest of the afternoon
        // along — but nothing is resequenced and nothing is taken away.
        var (problem, planned, rush) = ADayWithSomethingElseComingIn();

        var revised = Scheduler.Insert(planned, problem, rush.Id);
        var receiving = WhoTook(revised, rush.Id);

        var before = planned.RouteFor(receiving).Select(stop => stop.JobId);
        var after = revised.RouteFor(receiving).Select(stop => stop.JobId).Where(job => job != rush.Id);

        Assert.Equal(before, after);
    }

    [Fact]
    public void ReportsTheJobUnassignedWhenNobodyIsQualifiedForIt()
    {
        var problem = SmallCity.Problem()
            .Plus(SchedJobBuilder.Any().WithSkill("gas-safe").Build())
            .Build();
        var impossible = problem.Jobs[^1];
        var planned = Scheduler.Solve(SmallCity.Problem().Build());

        var revised = Scheduler.Insert(planned, problem, impossible.Id);

        Assert.Contains(impossible.Id, revised.Unassigned);
    }

    [Fact]
    public void ReportsTheJobUnassignedWhenItWillNotFitAnybodysRemainingHours()
    {
        // Every skill matched, every window fine, and still nowhere to put an eleven-hour job.
        var marathon = SchedJobBuilder.Any().WithSkill("hvac").Lasting(TimeSpan.FromHours(11)).Build();
        var problem = SmallCity.Problem().Plus(marathon).Build();
        var planned = Scheduler.Solve(SmallCity.Problem().Build());

        var revised = Scheduler.Insert(planned, problem, marathon.Id);

        Assert.Contains(marathon.Id, revised.Unassigned);
        Assert.Empty(HardConstraints.Violations(problem, revised, Travel));
    }

    [Fact]
    public void TakesTheJobOutOfTheUnassignedPileWhenItFindsSomewhereForIt()
    {
        // The other way an insertion arrives: not a job nobody has seen, but one an earlier
        // solve gave up on and a dispatcher is trying again. Leaving it in the pile would have
        // it drawn on the board twice, once on a timeline and once in the tray.
        var (problem, planned, rush) = ADayWithSomethingElseComingIn();
        var givenUpOn = new Solution(planned.Routes, [rush.Id], 0d);

        var revised = Scheduler.Insert(givenUpOn, problem, rush.Id);

        Assert.Empty(revised.Unassigned);
        Assert.Contains(rush.Id, revised.Routes.Values.SelectMany(route => route).Select(stop => stop.JobId));
    }

    [Fact]
    public void DoesNotReportAJobUnassignedTwiceWhenItStillCannotPlaceIt()
    {
        var marathon = SchedJobBuilder.Any().WithSkill("hvac").Lasting(TimeSpan.FromHours(11)).Build();
        var problem = SmallCity.Problem().Plus(marathon).Build();
        var givenUpOn = new Solution(Scheduler.Solve(SmallCity.Problem().Build()).Routes, [marathon.Id], 0d);

        var revised = Scheduler.Insert(givenUpOn, problem, marathon.Id);

        Assert.Equal(marathon.Id, Assert.Single(revised.Unassigned));
    }

    /// <summary>
    /// The promise <see cref="IScheduler.Insert"/> makes, against a day that was not built by the
    /// constructor: a stop stays at the time it was given.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A day that came out of a solve is already packed against the start of the shift, so re-timing
    /// it from scratch happens to reproduce it and nothing shows. A day a dispatcher arranged by
    /// hand is not: they told a customer two o'clock, and the gap in front of that stop is the
    /// promise, not slack. Timing the candidate run from the start of the shift pulls the stop
    /// forward into the gap — a customer who is not home, and, when the stop is one a technician is
    /// already driving to, a plan that moves under them.
    /// </para>
    /// <para>
    /// The clock may still move a stop <em>later</em>; that is what displacing an afternoon means,
    /// and <c>KeepsTheReceivingTechniciansOtherStopsInTheOrderTheyWereIn</c> covers it.
    /// </para>
    /// </remarks>
    [Fact]
    public void LeavesAStopAtTheTimeItWasPromisedRatherThanPullingItForward()
    {
        var day = new TimeWindow(SmallCity.At(8), SmallCity.At(18));
        var depot = new GeoPoint(51.5074d, -0.1278d);
        var promised = SmallCity.At(14);

        var afternoon = JobAt(new GeoPoint(51.5400d, -0.2000d), day);
        var rush = JobAt(new GeoPoint(51.5080d, -0.1280d), day);

        var technician = TechPlanBuilder.Any().Skilled("hvac").BasedAt(depot).Working(day).Build();
        var problem = SchedulingProblemBuilder.Any()
            .Over(day)
            .Staffed(technician)
            .Booked(afternoon, rush)
            .Build();

        // Placed by hand, not by the constructor: two o'clock, with the whole morning free.
        var byHand = new Solution(
            new Dictionary<TechnicianId, ImmutableArray<Stop>>
            {
                [technician.Id] =
                [
                    new Stop(afternoon.Id, promised, promised, promised + afternoon.Duration, TravelMin: 20d),
                ],
            },
            [],
            0d);

        var revised = Scheduler.Insert(byHand, problem, rush.Id);
        var route = revised.RouteFor(technician.Id);

        Assert.Equal(promised, route.Single(stop => stop.JobId == afternoon.Id).Start);
        Assert.Contains(route, stop => stop.JobId == rush.Id);
        Assert.Empty(HardConstraints.Violations(problem, revised, Travel));
    }

    [Fact]
    public void TakesTheSlotThatKeepsThePromiseRatherThanTheShortestDrive()
    {
        // A technician whose two jobs are both across town, next door to each other: one at
        // eight in the morning, one at three in the afternoon. The emergency is on the depot's
        // doorstep and has to be done between ten and eleven.
        //
        // Hanging it on the end of the run is much the shortest drive — the technician is
        // passing the door on the way home anyway — and gets there at ten past four, five
        // hours after the window shut. Slotting it between the two doubles the driving and
        // arrives at ten. Picking by mileage takes the first; pricing the whole objective
        // takes the second, which is the entire reason this weighs lateness at all.
        var day = new TimeWindow(SmallCity.At(8), SmallCity.At(18));
        var depot = new GeoPoint(51.5074d, -0.1278d);

        var morning = JobAt(new GeoPoint(51.5400d, -0.2000d), new TimeWindow(SmallCity.At(8), SmallCity.At(9)));
        var afternoon = JobAt(new GeoPoint(51.5410d, -0.2010d), new TimeWindow(SmallCity.At(15), SmallCity.At(17)));
        var rush = SchedJobBuilder.Any()
            .WithSkill("hvac")
            .WithPriority(JobPriority.Emergency)
            .At(new GeoPoint(51.5080d, -0.1280d))
            .InWindow(new TimeWindow(SmallCity.At(10), SmallCity.At(11)))
            .Lasting(TimeSpan.FromMinutes(45))
            .Build();

        var technician = TechPlanBuilder.Any()
            .Skilled("hvac")
            .BasedAt(depot)
            .Working(day)
            .Build();

        var problem = SchedulingProblemBuilder.Any()
            .Over(day)
            .Staffed(technician)
            .Booked(morning, afternoon, rush)
            .Build();

        var planned = Scheduler.Solve(
            SchedulingProblemBuilder.Any().Over(day).Staffed(technician).Booked(morning, afternoon).Build());

        var revised = Scheduler.Insert(planned, problem, rush.Id);
        var placed = revised.RouteFor(technician.Id).Single(stop => stop.JobId == rush.Id);

        Assert.True(
            placed.Start < rush.Window.End,
            $"the rush job was slotted in at {placed.Start:t}, after its window closed at {rush.Window.End:t}");
        Assert.Equal(1, revised.RouteFor(technician.Id).IndexOf(placed));
    }

    [Fact]
    public void PicksTheCheapestOfTheSlotsThatWouldWork()
    {
        // Checked exhaustively rather than by eye: every other position on every other
        // technician is priced, and none of them is better than the one chosen.
        var (problem, planned, rush) = ADayWithSomethingElseComingIn();
        var objective = new ObjectiveEvaluator(problem, Travel);

        var chosen = objective.Evaluate(Scheduler.Insert(planned, problem, rush.Id)).Total;

        Assert.All(
            EverySlotFor(problem, planned, rush),
            candidate => Assert.True(
                chosen <= objective.Evaluate(candidate).Total + 1e-9d,
                $"a slot costing {objective.Evaluate(candidate).Total:F1} was passed over for one costing {chosen:F1}"));
    }

    [Fact]
    public void BothSchedulersSlotItIntoTheSamePlace()
    {
        // Insertion is not a search, so there is nothing for annealing to do differently.
        var (problem, planned, rush) = ADayWithSomethingElseComingIn();

        // Reached through the interface, which is how the layer above will reach it.
        IScheduler[] schedulers = [new GreedyScheduler(Travel), new AnnealingScheduler(Travel)];

        var days = schedulers
            .Select(scheduler => Describe(problem, scheduler.Insert(planned, problem, rush.Id)))
            .ToList();

        Assert.Equal(days[0], days[1]);
    }

    [Fact]
    public void ReportsTheRealCostOfTheDayItHandsBack()
    {
        var (problem, planned, rush) = ADayWithSomethingElseComingIn();

        var revised = Scheduler.Insert(planned, problem, rush.Id);

        Assert.Equal(revised.Cost, new ObjectiveEvaluator(problem, Travel).Evaluate(revised).Total, 6);
    }

    [Fact]
    public void RefusesAJobThatIsAlreadyOnSomebodysDay()
    {
        // Not a no-op: a caller asking this has lost track of what is scheduled, and quietly
        // returning the day unchanged would let them carry on believing it.
        var problem = SmallCity.Problem().Build();
        var planned = Scheduler.Solve(problem);

        Assert.Throws<ArgumentException>(
            () => Scheduler.Insert(planned, problem, SmallCity.FitzroviaBoiler.Id));
    }

    [Fact]
    public void RefusesAJobTheProblemHasNeverHeardOf()
    {
        var problem = SmallCity.Problem().Build();
        var planned = Scheduler.Solve(problem);

        Assert.Throws<ArgumentException>(() => Scheduler.Insert(planned, problem, JobId.New()));
    }

    /// <summary>The small city as planned, plus an emergency that has just come in.</summary>
    private static (SchedulingProblem Problem, Solution Planned, SchedJob Rush) ADayWithSomethingElseComingIn()
    {
        var rush = SchedJobBuilder.Any()
            .WithSkill("hvac")
            .WithPriority(JobPriority.Emergency)
            .At(new GeoPoint(51.5250d, -0.1100d))
            .InWindow(new TimeWindow(SmallCity.At(11), SmallCity.At(14)))
            .Lasting(TimeSpan.FromMinutes(45))
            .Build();

        return (
            SmallCity.Problem().Plus(rush).Build(),
            Scheduler.Solve(SmallCity.Problem().Build()),
            rush);
    }

    /// <summary>Every day the job could have produced, one per technician and position.</summary>
    private static IEnumerable<Solution> EverySlotFor(SchedulingProblem problem, Solution planned, SchedJob rush)
    {
        var jobs = problem.Jobs.ToDictionary(job => job.Id);
        var distances = TravelMatrix.For(problem, Travel);

        foreach (var technician in problem.Technicians)
        {
            var run = planned.RouteFor(technician.Id).Select(stop => jobs[stop.JobId]).ToList();

            for (var position = 0; position <= run.Count; position++)
            {
                var candidate = new List<SchedJob>(run);
                candidate.Insert(position, rush);

                if (RouteTimer.Time(technician, candidate, distances) is not { } stops)
                {
                    continue;
                }

                var routes = problem.Technicians.ToDictionary(
                    other => other.Id,
                    other => other.Id == technician.Id ? stops : planned.RouteFor(other.Id));

                yield return new Solution(routes, planned.Unassigned.Where(job => job != rush.Id), 0d);
            }
        }
    }

    private static TechnicianId WhoTook(Solution solution, JobId job) =>
        solution.Routes.Single(route => route.Value.Any(stop => stop.JobId == job)).Key;

    private static SchedJob JobAt(GeoPoint where, TimeWindow window) =>
        SchedJobBuilder.Any()
            .WithSkill("hvac")
            .At(where)
            .InWindow(window)
            .Lasting(TimeSpan.FromMinutes(45))
            .Build();

    private static string Describe(SchedulingProblem problem, Solution solution) =>
        string.Join(
            "\n",
            problem.Technicians.Select(technician => $"{technician.Id.Value}: " + string.Join(
                " -> ",
                solution.RouteFor(technician.Id).Select(stop => $"{stop.JobId.Value}@{stop.Start:O}"))));
}
