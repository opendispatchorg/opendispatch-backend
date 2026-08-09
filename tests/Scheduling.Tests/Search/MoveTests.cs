using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.Scheduling.Search;
using OpenDispatch.Scheduling.Travel;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;
using OpenDispatch.TestSupport.Fixtures;

namespace OpenDispatch.Scheduling.Tests.Search;

/// <summary>
/// The three ways the search is allowed to change its mind. Each is here because the others
/// cannot reach where it goes, so a move that quietly stops working would not break anything —
/// it would just leave the engine unable to find certain kinds of better day.
/// </summary>
/// <remarks>
/// Every scenario is arranged so that only driving separates the good arrangement from the
/// bad: windows span the whole day and shifts are long, so nothing is late and nobody is on
/// overtime. That way an improvement is unambiguously the move's doing.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class MoveTests
{
    private static readonly HaversineTravelTimeProvider Travel = new();

    private static readonly TimeWindow Day = new(SmallCity.At(8), SmallCity.At(18));

    // A depot with work due east and due north of it, and a job sitting between those two.
    private static readonly GeoPoint Depot = new(51.5000d, -0.1000d);
    private static readonly GeoPoint East = new(51.5000d, -0.0400d);
    private static readonly GeoPoint North = new(51.5600d, -0.1000d);
    private static readonly GeoPoint Between = new(51.5300d, -0.0550d);
    private static readonly GeoPoint FarWest = new(51.5000d, -0.3000d);

    [Fact]
    public void RelocatingAJobToATechnicianNearerToItLowersTheCost()
    {
        var eastEnd = Technician(East);
        var westEnd = Technician(FarWest);
        var nextDoorToWest = JobAt(new GeoPoint(51.5010d, -0.3010d));

        // The west-end job has been dumped on the east-end technician, who has to drive across
        // the whole city and back for it.
        var state = StateOf([eastEnd, westEnd], [nextDoorToWest], (eastEnd, [nextDoorToWest]), (westEnd, []));
        var before = state.Cost.Total;

        var outcome = state.Try(new Relocate(eastEnd.Id, 0, westEnd.Id, 0));

        Assert.NotNull(outcome);
        Assert.True(outcome.Cost.Total < before, $"{outcome.Cost.Total} should be less than {before}");
    }

    [Fact]
    public void RelocatingAStopLaterInTheSameDayLowersTheCostOfADetour()
    {
        // North, then east, then the job between the two — so the technician drives past the
        // middle job on the way out and comes back for it. Moving it up one place removes the
        // detour.
        var technician = Technician(Depot);
        var north = JobAt(North);
        var east = JobAt(East);
        var between = JobAt(Between);

        var state = StateOf([technician], [north, east, between], (technician, [north, east, between]));
        var before = state.Cost.Total;

        var outcome = state.Try(new Relocate(technician.Id, 2, technician.Id, 1));

        Assert.NotNull(outcome);
        Assert.True(outcome.Cost.Total < before, $"{outcome.Cost.Total} should be less than {before}");
    }

    [Fact]
    public void SwappingTwoJobsThatAreOnEachOthersTechniciansLowersTheCost()
    {
        // The move relocate cannot make in one step: each day is already the right length, so
        // every single relocate between them makes one of the two worse before it gets better.
        var eastEnd = Technician(East);
        var westEnd = Technician(FarWest);
        var eastJob = JobAt(new GeoPoint(51.5010d, -0.0410d));
        var westJob = JobAt(new GeoPoint(51.5010d, -0.3010d));

        var state = StateOf(
            [eastEnd, westEnd],
            [eastJob, westJob],
            (eastEnd, [westJob]),
            (westEnd, [eastJob]));
        var before = state.Cost.Total;

        var outcome = state.Try(new Swap(eastEnd.Id, 0, westEnd.Id, 0));

        Assert.NotNull(outcome);
        Assert.True(outcome.Cost.Total < before, $"{outcome.Cost.Total} should be less than {before}");
    }

    [Fact]
    public void ReversingASegmentUntanglesARouteThatCrossesItself()
    {
        // Three corners of a square driven in an order that crosses the middle. Reversing the
        // first two stops walks the perimeter instead.
        var technician = Technician(Depot);
        var corner = JobAt(new GeoPoint(51.5600d, -0.0400d));
        var east = JobAt(East);
        var north = JobAt(North);

        var state = StateOf([technician], [corner, east, north], (technician, [corner, east, north]));
        var before = state.Cost.Total;

        var outcome = state.Try(new TwoOpt(technician.Id, 0, 1));

        Assert.NotNull(outcome);
        Assert.True(outcome.Cost.Total < before, $"{outcome.Cost.Total} should be less than {before}");
    }

    [Fact]
    public void RefusesToRelocateAJobOntoATechnicianWhoCannotDoIt()
    {
        var plumber = Technician(Depot, "plumbing");
        var electrician = Technician(East, "electrical");
        var wiring = JobAt(East, "electrical");

        var state = StateOf([plumber, electrician], [wiring], (plumber, []), (electrician, [wiring]));

        Assert.Null(state.Try(new Relocate(electrician.Id, 0, plumber.Id, 0)));
    }

    [Fact]
    public void RefusesAMoveThatWouldPushWorkPastTheEndOfAShift()
    {
        // Two two-hour jobs and a technician who works four: they fit where they are, one
        // each, and not together.
        var morning = Technician(Depot, shift: new TimeWindow(SmallCity.At(8), SmallCity.At(12)));
        var other = Technician(East);
        var first = JobAt(East, duration: TimeSpan.FromHours(2));
        var second = JobAt(North, duration: TimeSpan.FromHours(2));

        var state = StateOf([morning, other], [first, second], (morning, [first]), (other, [second]));

        Assert.Null(state.Try(new Relocate(other.Id, 0, morning.Id, 1)));
    }

    [Fact]
    public void AProposedMoveChangesNothingUntilItIsTaken()
    {
        // The whole reason the search needs no undo: it can look at the price of a worse day
        // and walk away.
        var technician = Technician(Depot);
        var north = JobAt(North);
        var east = JobAt(East);
        var between = JobAt(Between);

        var state = StateOf([technician], [north, east, between], (technician, [north, east, between]));
        var before = state.Cost;

        var outcome = state.Try(new Relocate(technician.Id, 2, technician.Id, 1));

        Assert.NotNull(outcome);
        Assert.Equal(before, state.Cost);
        Assert.Equal([north.Id, east.Id, between.Id], state.RunOf(technician.Id).Select(job => job.Id));

        state.Commit(outcome);

        Assert.Equal(outcome.Cost, state.Cost);
        Assert.Equal([north.Id, between.Id, east.Id], state.RunOf(technician.Id).Select(job => job.Id));
    }

    [Fact]
    public void TheRunningCostStillMatchesAFreshValuationAfterASeriesOfMoves()
    {
        // The state re-prices only the days a move touches and adds the rest up from what it
        // already knew. If that bookkeeping drifted from the truth, every decision the search
        // made afterwards would be made on a number nobody had checked.
        var problem = SmallCity.Problem().Build();
        var solution = new GreedyScheduler(Travel).Solve(problem);
        var state = SearchState.From(problem, TravelMatrix.For(problem, Travel), solution);

        var taken = 0;

        foreach (var move in Moves(SmallCity.Sam.Id, SmallCity.Alex.Id))
        {
            if (state.Try(move) is { } outcome)
            {
                state.Commit(outcome);
                taken++;
            }
        }

        var fresh = new ObjectiveEvaluator(problem, Travel).Evaluate(state.ToSolution());

        Assert.True(taken > 0, "no move was applicable, so the bookkeeping was never exercised");
        Assert.Equal(fresh.Total, state.Cost.Total, 6);
        Assert.Empty(HardConstraints.Violations(problem, state.ToSolution(), Travel));
    }

    [Fact]
    public void EveryMoveLeavesASolutionAnybodyCouldActuallyDrive()
    {
        // Whatever the search does to a day, what comes out the other end still has to obey
        // the skills, the shifts and the driving.
        var problem = SmallCity.Problem().Build();
        var state = SearchState.From(
            problem,
            TravelMatrix.For(problem, Travel),
            new GreedyScheduler(Travel).Solve(problem));

        var taken = 0;

        foreach (var move in Moves(SmallCity.Sam.Id, SmallCity.Jordan.Id))
        {
            if (state.Try(move) is not { } outcome)
            {
                continue;
            }

            state.Commit(outcome);
            taken++;

            Assert.Empty(HardConstraints.Violations(problem, state.ToSolution(), Travel));
        }

        Assert.True(taken > 0, "no move was applicable, so nothing was checked");
    }

    [Fact]
    public void NeverProposesHandingWorkToSomebodyUnqualifiedForIt()
    {
        // Redrawing instead of proposing the impossible is only ever an efficiency, so nothing
        // else in this file would notice if it stopped happening — the search would simply do
        // a fraction of the work its iteration count suggests. On a shop with specialised
        // technicians it was 38% of every proposal.
        var problem = SmallCity.Problem().Build();
        var state = SearchState.From(
            problem,
            TravelMatrix.For(problem, Travel),
            new GreedyScheduler(Travel).Solve(problem));
        var generator = new MoveGenerator(problem, new Random(problem.Seed));
        var technicians = problem.Technicians.ToDictionary(technician => technician.Id);

        var proposed = 0;

        for (var draw = 0; draw < 2_000; draw++)
        {
            if (generator.Propose(state) is not { } move)
            {
                continue;
            }

            proposed++;

            var candidates = move.Touches.ToDictionary(
                technician => technician,
                technician => new List<SchedJob>(state.RunOf(technician)));
            move.RewriteIn(candidates);

            foreach (var (technician, run) in candidates)
            {
                Assert.All(
                    run,
                    job => Assert.True(
                        technicians[technician].HasSkill(job.RequiredSkill),
                        $"{technician.Value} was offered '{job.RequiredSkill}' work they cannot do"));
            }
        }

        Assert.True(proposed > 100, $"only {proposed} moves were proposed, so little was checked");
    }

    [Fact]
    public void RefusesAReversalThatWouldNotRearrangeAnything()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TwoOpt(SmallCity.Sam.Id, 1, 1));
    }

    [Fact]
    public void RefusesAPositionThatCannotExist()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Relocate(SmallCity.Sam.Id, -1, SmallCity.Alex.Id, 0));
    }

    private static IEnumerable<Move> Moves(Domain.Identifiers.TechnicianId first, Domain.Identifiers.TechnicianId second)
    {
        yield return new Relocate(first, 0, second, 0);
        yield return new Swap(first, 0, second, 0);
        yield return new TwoOpt(first, 0, 1);
        yield return new Relocate(second, 1, first, 0);
        yield return new Swap(second, 0, second, 1);
    }

    private static TechPlan Technician(GeoPoint homeBase, string skill = "hvac", TimeWindow? shift = null) =>
        TechPlanBuilder.Any().Skilled(skill).BasedAt(homeBase).Working(shift ?? Day).Build();

    private static SchedJob JobAt(GeoPoint where, string skill = "hvac", TimeSpan? duration = null) =>
        SchedJobBuilder.Any()
            .At(where)
            .WithSkill(skill)
            .InWindow(Day)
            .Lasting(duration ?? TimeSpan.FromMinutes(30))
            .Build();

    private static SearchState StateOf(
        TechPlan[] technicians,
        SchedJob[] jobs,
        params (TechPlan Technician, SchedJob[] Run)[] days) =>
        SearchStates.Of(
            SchedulingProblemBuilder.Any().Over(Day).Staffed(technicians).Booked(jobs).Build(),
            Travel,
            days);
}
