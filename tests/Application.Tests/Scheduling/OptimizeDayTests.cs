using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs.AssignJob;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Results;
using OpenDispatch.Application.Scheduling.OptimizeDay;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Application.Technicians.SetSkills;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Scheduling;

/// <summary>
/// The engine over real rows: a day loaded out of the repositories, solved, and written back as
/// stops.
/// </summary>
/// <remarks>
/// The search itself is exhaustively tested in <c>Scheduling.Tests</c> against problems written by
/// hand, and none of that is restated here. What this slice adds is the translation either side of
/// it — jobs and technicians into a problem, a solution into assignments and statuses — and the
/// two properties that only exist once the two halves are joined: the same day plans the same way
/// twice, and a job the engine cannot place is left with no stop and no plan.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class OptimizeDayTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeWindow Day = new(MondayMorning, MondayMorning.AddHours(9));

    private static readonly GeoPoint Depot = new(51.5074d, -0.1278d);
    private static readonly GeoPoint Slough = new(51.5107d, -0.5950d);
    private static readonly GeoPoint Croydon = new(51.3762d, -0.0982d);
    private static readonly GeoPoint Camden = new(51.5390d, -0.1426d);

    /// <summary>Twelve stops around London — enough work that the search has real choices to make.</summary>
    private static readonly GeoPoint[] ABusyDay =
    [
        Slough,
        Croydon,
        Camden,
        new(51.4816d, -0.0076d),
        new(51.5450d, -0.0553d),
        new(51.4613d, -0.1156d),
        new(51.5985d, -0.1075d),
        new(51.4934d, 0.0098d),
        new(51.5074d, -0.3278d),
        new(51.4123d, -0.3007d),
        new(51.5698d, -0.2437d),
        new(51.4409d, 0.1663d),
    ];

    [Fact]
    public async Task PlansEveryJobItCanAndTurnsThemIntoAScheduledDay()
    {
        await using var slice = SliceHost.Dispatching();
        await ATechnician(slice);
        var work = new[]
        {
            await ABookedJob(slice, Slough),
            await ABookedJob(slice, Croydon),
            await ABookedJob(slice, Camden),
        };

        var optimized = await Optimize(slice);

        Assert.True(optimized.IsSuccess);
        Assert.Equal(3, optimized.Value.Planned);
        Assert.Empty(optimized.Value.Unassigned);

        var stops = slice.Store<Assignment>().Saved;
        Assert.Equal(3, stops.Count);
        Assert.Equal(
            work.Select(job => job.Value).Order(),
            stops.Select(stop => stop.JobId.Value).Order());

        // Every stop is a real placement: sequenced from zero, and driven to from somewhere.
        Assert.Equal([0, 1, 2], stops.Select(stop => stop.Sequence).Order());
        Assert.All(stops, stop => Assert.True(stop.TravelMin >= 0d));
        Assert.All(stops, stop => Assert.True(stop.ScheduledStart >= Day.Start));

        // Demand became a plan. Nothing else about the jobs moved.
        Assert.All(slice.Store<Job>().Saved, job => Assert.Equal(JobStatus.Scheduled, job.Status));
    }

    /// <summary>
    /// The step's own requirement. The seed is fixed in the handler, so the same rows must produce
    /// the same day however many times a dispatcher presses the button.
    /// </summary>
    /// <remarks>
    /// The day is deliberately a busy one — twelve stops over two technicians, spread across the
    /// city — because a small day plans the same way whatever the seed, and a test over one would
    /// pass just as happily against a handler that seeded itself from the clock.
    /// </remarks>
    [Fact]
    public async Task RunningItTwiceOverAnUnchangedDayPlansItTheSameWay()
    {
        await using var slice = SliceHost.Dispatching();
        await ATechnician(slice, "Sam Rivera");
        await ATechnician(slice, "Ada Okafor");

        foreach (var where in ABusyDay)
        {
            await ABookedJob(slice, where);
        }

        var first = await Optimize(slice);
        var afterFirst = Plan(slice);

        var second = await Optimize(slice);
        var afterSecond = Plan(slice);

        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(first.Value.Cost, second.Value.Cost);
        Assert.Equal(first.Value.Planned, second.Value.Planned);

        // And it rewrote the stops it already had rather than adding a second set.
        Assert.Equal(ABusyDay.Length, slice.Store<Assignment>().Saved.Count + first.Value.Unassigned.Count);
    }

    /// <summary>
    /// A hard constraint bit: nobody holds the skill. Lateness is soft, so this is nearly the only
    /// way a job goes unplaced — and it must be visible rather than silent.
    /// </summary>
    [Fact]
    public async Task LeavesWorkNobodyIsQualifiedForUnassignedAndUnscheduled()
    {
        await using var slice = SliceHost.Dispatching();
        await ATechnician(slice);
        var hvac = await ABookedJob(slice, Slough);
        var gas = await ABookedJob(slice, Croydon, skill: "gas safe");

        var optimized = await Optimize(slice);

        Assert.Equal(gas, Assert.Single(optimized.Value.Unassigned));
        Assert.Equal(1, optimized.Value.Planned);

        var stop = Assert.Single(slice.Store<Assignment>().Saved);
        Assert.Equal(hvac, stop.JobId);

        var unplaceable = Assert.Single(slice.Store<Job>().Saved, job => job.Id == gas);
        Assert.Equal(JobStatus.Unscheduled, unplaceable.Status);
    }

    /// <summary>
    /// The only deletion in the system, and the reason the port has a <c>Remove</c>: an orphaned
    /// stop would show on the board as work somebody is expected to drive to.
    /// </summary>
    /// <remarks>
    /// The job goes back to being demand at the same time, which is the other half of withdrawing
    /// a plan. Without it the board shows the job in the unassigned pile still labelled
    /// <c>Scheduled</c> — the one thing on that screen that would be false.
    /// </remarks>
    [Fact]
    public async Task DropsTheStopOfAJobItCanNoLongerPlaceAndPutsTheJobBackInThePile()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);
        var job = await ABookedJob(slice, Slough);
        await slice.Send(new AssignJobCommand(job, technician, MondayMorning.AddHours(1)));
        Assert.Single(slice.Store<Assignment>().Saved);

        // The crew loses the qualification the job needs, so the engine can no longer place it.
        await slice.Send(new SetSkillsCommand(technician, ["plumbing"]));

        var optimized = await Optimize(slice);

        Assert.Equal(job, Assert.Single(optimized.Value.Unassigned));
        Assert.Empty(slice.Store<Assignment>().Saved);

        var withdrawn = Assert.Single(slice.Store<Job>().Saved, saved => saved.Id == job);
        Assert.Equal(JobStatus.Unscheduled, withdrawn.Status);
        Assert.IsType<JobUnscheduled>(withdrawn.DomainEvents[^1]);
    }

    /// <summary>
    /// The same, from a job that had already been sent to a phone. It is the case worth naming:
    /// the technician holding it is told by step 43's pull, which reports the stop as gone — so
    /// the status and the phone agree rather than the job insisting it is planned.
    /// </summary>
    [Fact]
    public async Task WithdrawsWorkEvenAfterItHasReachedAPhone()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);
        var job = await ABookedJob(slice, Slough);
        await slice.Send(new AssignJobCommand(job, technician, MondayMorning.AddHours(1)));
        await slice.Send(new ChangeJobStatusCommand(job, JobStatus.Dispatched));

        await slice.Send(new SetSkillsCommand(technician, ["plumbing"]));

        await Optimize(slice);

        Assert.Equal(
            JobStatus.Unscheduled,
            Assert.Single(slice.Store<Job>().Saved, saved => saved.Id == job).Status);
    }

    /// <summary>
    /// A job that was never planned has no plan to withdraw, so nothing announces anything about
    /// it — the optimiser simply could not place it this time either.
    /// </summary>
    [Fact]
    public async Task SaysNothingAboutWorkThatWasAlreadyWaiting()
    {
        await using var slice = SliceHost.Dispatching();
        await ATechnician(slice);
        var gas = await ABookedJob(slice, Croydon, skill: "gas safe");

        await Optimize(slice);

        var waiting = Assert.Single(slice.Store<Job>().Saved, saved => saved.Id == gas);
        Assert.Equal(JobStatus.Unscheduled, waiting.Status);
        Assert.Empty(waiting.DomainEvents);
    }

    [Fact]
    public async Task PlansNothingWhenThereIsNobodyToPlanItOn()
    {
        await using var slice = SliceHost.Dispatching();
        var job = await ABookedJob(slice, Slough);

        var optimized = await Optimize(slice);

        Assert.Equal(0, optimized.Value.Planned);
        Assert.Equal(job, Assert.Single(optimized.Value.Unassigned));
        Assert.Empty(slice.Store<Assignment>().Saved);
    }

    /// <summary>
    /// Work already under way is not the optimiser's to move, and the answer comes from the
    /// domain's own list rather than a status check written here.
    /// </summary>
    [Fact]
    public async Task LeavesWorkThatIsAlreadyBeingDrivenToAlone()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);
        var underway = await ABookedJob(slice, Slough);
        await slice.Send(new AssignJobCommand(underway, technician, MondayMorning.AddHours(1)));

        foreach (var status in new[] { JobStatus.Dispatched, JobStatus.EnRoute })
        {
            await slice.Send(new ChangeJobStatusCommand(underway, status));
        }

        var planned = Assert.Single(slice.Store<Assignment>().Saved);
        var wasAt = planned.ScheduledStart;

        var optimized = await Optimize(slice);

        Assert.Equal(0, optimized.Value.Planned);
        Assert.Empty(optimized.Value.Unassigned);

        // Its stop is untouched: the engine was never told about the job at all.
        Assert.Equal(wasAt, Assert.Single(slice.Store<Assignment>().Saved).ScheduledStart);
        Assert.Equal(JobStatus.EnRoute, Assert.Single(slice.Store<Job>().Saved, job => job.Id == underway).Status);
    }

    /// <summary>
    /// A stop records when the work starts, not when the van pulls up — which are different
    /// instants only when a technician arrives before the customer's window opens and waits.
    /// </summary>
    /// <remarks>
    /// It matters twice over. A dispatcher dragging the same stop by hand enters the time work
    /// begins, so recording an arrival here would give one field two meanings; and lateness is
    /// measured at the start of work, which step 20's board projection promises can be derived
    /// from this field alone.
    /// </remarks>
    [Fact]
    public async Task RecordsWhenTheWorkStartsRatherThanWhenTheVanArrives()
    {
        await using var slice = SliceHost.Dispatching();
        await ATechnician(slice);

        // Promised for the afternoon, on a site twenty minutes away: the technician can be there
        // long before the customer is expecting them.
        var afternoon = new TimeWindow(MondayMorning.AddHours(6), MondayMorning.AddHours(8));
        await ABookedJob(slice, Slough, window: afternoon);

        await Optimize(slice);

        var stop = Assert.Single(slice.Store<Assignment>().Saved);
        Assert.Equal(afternoon.Start, stop.ScheduledStart);
    }

    [Fact]
    public async Task RefusesAHorizonThatEndsBeforeItStarts()
    {
        await using var slice = SliceHost.Dispatching();

        var optimized = await slice.Send(
            new OptimizeDayCommand(MondayMorning, MondayMorning.AddHours(-1)));

        var failure = Assert.IsType<ValidationError>(optimized.Error);
        Assert.Equal(
            "A horizon cannot end before it starts.",
            Assert.Single(failure.Failures[nameof(OptimizeDayCommand.To)]));
    }

    /// <summary>
    /// A negative price is a reward for the thing it is meant to discourage, and the engine refuses
    /// one by throwing — so this rule is what stands between a mistyped weight and a 500.
    /// </summary>
    [Theory]
    [InlineData(-1d, 5d, 2d, 500d)]
    [InlineData(1d, double.NaN, 2d, 500d)]
    [InlineData(1d, 5d, 2d, double.PositiveInfinity)]
    public async Task RefusesAWeightThatIsNotAPrice(double travel, double late, double overtime, double dropped)
    {
        await using var slice = SliceHost.Dispatching();

        var optimized = await slice.Send(new OptimizeDayCommand(
            Day.Start,
            Day.End,
            new ObjectiveWeights(travel, late, overtime, dropped)));

        Assert.IsType<ValidationError>(optimized.Error);
    }

    [Fact]
    public async Task TakesTheWeightsItIsGiven()
    {
        await using var slice = SliceHost.Dispatching();
        await ATechnician(slice);
        await ABookedJob(slice, Slough);

        // Driving priced at nothing, so the plan's cost is whatever is left. It still places the
        // work — weights change what a good day is, not what a possible one is.
        var optimized = await slice.Send(new OptimizeDayCommand(
            Day.Start,
            Day.End,
            new ObjectiveWeights(Travel: 0d, Lateness: 0d, Overtime: 0d, Unassigned: 0d)));

        Assert.Equal(1, optimized.Value.Planned);
        Assert.Equal(0d, optimized.Value.Cost);
    }

    /// <summary>The plan as a comparable shape: who goes where, when, in what order.</summary>
    private static IReadOnlyList<(JobId Job, TechnicianId Technician, DateTimeOffset Start, int Sequence)> Plan(
        SliceHost slice) =>
        [
            .. slice.Store<Assignment>().Saved
                .Select(stop => (stop.JobId, stop.TechnicianId, stop.ScheduledStart, stop.Sequence))
                .OrderBy(stop => stop.JobId.Value),
        ];

    private static Task<Result<OptimizedDay>> Optimize(SliceHost slice) =>
        slice.Send(new OptimizeDayCommand(Day.Start, Day.End));

    private static async Task<TechnicianId> ATechnician(SliceHost slice, string name = "Sam Rivera")
    {
        var technician = await slice.Send(new CreateTechnicianCommand(
            name,
            ["hvac"],
            Day.Start,
            Day.End,
            Depot.Lat,
            Depot.Lng));

        return technician.Value;
    }

    private static async Task<JobId> ABookedJob(
        SliceHost slice,
        GeoPoint where,
        string skill = "hvac",
        TimeWindow? window = null)
    {
        var customer = await slice.Send(new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await slice.Send(new AddServiceLocationCommand(
            customer.Value,
            "Site",
            "12 Bath Road",
            where.Lat,
            where.Lng));

        var job = await slice.Send(new CreateJobCommand(
            customer.Value,
            location.Value,
            skill,
            JobPriority.Normal,
            (window ?? Day).Start,
            (window ?? Day).End,
            TimeSpan.FromHours(1)));

        return job.Value;
    }
}
