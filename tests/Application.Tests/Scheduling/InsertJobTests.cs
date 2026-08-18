using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Jobs.AssignJob;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Results;
using OpenDispatch.Application.Scheduling;
using OpenDispatch.Application.Scheduling.InsertJob;
using OpenDispatch.Application.Scheduling.OptimizeDay;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Scheduling;

/// <summary>
/// Emergency dispatch: one job slotted into a day that already exists.
/// </summary>
/// <remarks>
/// The search is tested in <c>Scheduling.Tests</c> against days written by hand. What this slice
/// adds is the round trip the engine cannot do for itself — stored stops turned back into a
/// <c>Solution</c>, and only what moved written back — so that is what these are about: the rest of
/// the board stays exactly where it was, and a job that will not fit is refused rather than
/// half-placed.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class InsertJobTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeWindow Day = new(MondayMorning, MondayMorning.AddHours(9));

    private static readonly GeoPoint Depot = new(51.5074d, -0.1278d);
    private static readonly GeoPoint Slough = new(51.5107d, -0.5950d);
    private static readonly GeoPoint Croydon = new(51.3762d, -0.0982d);
    private static readonly GeoPoint Camden = new(51.5390d, -0.1426d);

    /// <summary>Enough work that two technicians are both busy with it.</summary>
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
    ];

    [Fact]
    public async Task SlotsAnEmergencyIntoADayThatIsAlreadyPlanned()
    {
        await using var slice = SliceHost.Dispatching();
        await ATechnician(slice);
        await ABookedJob(slice, Slough);
        await ABookedJob(slice, Croydon);
        await slice.Send(new OptimizeDayCommand(Day.Start, Day.End));

        var emergency = await ABookedJob(slice, Camden, JobPriority.Emergency);
        var inserted = await slice.Send(new InsertJobCommand(emergency));

        Assert.True(inserted.IsSuccess);

        var stop = Assert.Single(slice.Store<Assignment>().Saved, planned => planned.JobId == emergency);
        Assert.Equal(inserted.Value.AssignmentId, stop.Id);
        Assert.Equal(inserted.Value.TechnicianId, stop.TechnicianId);
        Assert.Equal(inserted.Value.ScheduledStart, stop.ScheduledStart);
        Assert.Equal(inserted.Value.Sequence, stop.Sequence);

        // Three stops on the day now, and the emergency is one of them.
        Assert.Equal(3, slice.Store<Assignment>().Saved.Count);
        Assert.Equal(JobStatus.Scheduled, Assert.Single(slice.Store<Job>().Saved, job => job.Id == emergency).Status);
    }

    /// <summary>
    /// The step's own requirement, and the difference between this and a re-optimise: every stop
    /// that was already planned keeps its technician and its place in the run.
    /// </summary>
    [Fact]
    public async Task LeavesTheStopsItDidNotHaveToMoveExactlyWhereTheyWere()
    {
        await using var slice = SliceHost.Dispatching();
        await ATechnician(slice, "Sam Rivera");
        await ATechnician(slice, "Ada Okafor");

        foreach (var site in ABusyDay)
        {
            await ABookedJob(slice, site);
        }

        await slice.Send(new OptimizeDayCommand(Day.Start, Day.End));
        var before = Plan(slice);

        // Both technicians are busy, so "the other lane is untouched" has something to prove.
        Assert.Equal(2, before.GroupBy(stop => stop.Technician).Count());

        var emergency = await ABookedJob(slice, Camden, JobPriority.Emergency);
        var inserted = await slice.Send(new InsertJobCommand(emergency));

        var after = Plan(slice);
        var moved = before.Where(stop => !after.Contains(stop)).ToList();

        // Whatever moved is on the technician who took the emergency, and there are exactly as
        // many of them as the answer said were displaced.
        Assert.All(moved, stop => Assert.Equal(inserted.Value.TechnicianId, stop.Technician));
        Assert.Equal(inserted.Value.Displaced, moved.Count);

        // Nobody else's day was touched at all.
        Assert.Equal(
            before.Where(stop => stop.Technician != inserted.Value.TechnicianId),
            after.Where(stop => stop.Technician != inserted.Value.TechnicianId && stop.Job != emergency));
    }

    /// <summary>
    /// A stop that does not move stays silent. Both planning slices rewrite whole days, so without
    /// this an insertion would announce every stop on the board as changed and repaint all of it.
    /// </summary>
    [Fact]
    public async Task AnnouncesOnlyTheStopsThatActuallyMoved()
    {
        await using var slice = SliceHost.Dispatching();
        await ATechnician(slice, "Sam Rivera");
        await ATechnician(slice, "Ada Okafor");

        foreach (var site in ABusyDay)
        {
            await ABookedJob(slice, site);
        }

        await slice.Send(new OptimizeDayCommand(Day.Start, Day.End));

        foreach (var stop in slice.Store<Assignment>().Saved)
        {
            stop.ClearDomainEvents();
        }

        var emergency = await ABookedJob(slice, Camden, JobPriority.Emergency);
        var inserted = await slice.Send(new InsertJobCommand(emergency));

        var announced = slice.Store<Assignment>().Saved
            .Where(stop => stop.DomainEvents.Count > 0)
            .ToList();

        Assert.All(announced, stop => Assert.Equal(inserted.Value.TechnicianId, stop.TechnicianId));

        // The displaced stops, plus the emergency's own — which is new rather than moved, and says
        // so with an AssignmentPlanned. It is not counted in Displaced, because "how much of my
        // afternoon just changed" is a question about work that was already planned.
        Assert.Equal(inserted.Value.Displaced + 1, announced.Count);
        Assert.Single(announced, stop => stop.DomainEvents.OfType<AssignmentPlanned>().Any());

        // Not a vacuous pass: the emergency did push somebody's afternoon along, and the stops on
        // the other technician's lane said nothing at all.
        Assert.True(inserted.Value.Displaced > 0);
        Assert.True(slice.Store<Assignment>().Saved.Count > announced.Count + 1);
    }

    /// <summary>
    /// The infeasible case the step asks for. Failing rather than reporting an empty answer also
    /// rolls the transaction back, so a refused emergency leaves the board as it was.
    /// </summary>
    [Fact]
    public async Task ReportsAFailureWhenTheWorkWillNotFitAnywhere()
    {
        await using var slice = SliceHost.Dispatching();
        await ATechnician(slice);
        var gas = await ABookedJob(slice, Slough, skill: "gas safe");

        var inserted = await slice.Send(new InsertJobCommand(gas));

        Assert.True(inserted.IsFailure);
        Assert.Equal(SchedulingErrors.CouldNotPlaceCode, inserted.Error!.Code);
        Assert.Equal(ErrorCategory.Conflict, inserted.Error.Category);

        Assert.Empty(slice.Store<Assignment>().Saved);
        Assert.Equal(JobStatus.Unscheduled, Assert.Single(slice.Store<Job>().Saved).Status);
    }

    [Fact]
    public async Task RefusesAJobThatIsAlreadyOnSomebodysDay()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);
        var job = await ABookedJob(slice, Slough);
        await slice.Send(new AssignJobCommand(job, technician, MondayMorning.AddHours(1)));

        var inserted = await slice.Send(new InsertJobCommand(job));

        Assert.Equal(SchedulingErrors.AlreadyPlannedCode, inserted.Error!.Code);
        Assert.Single(slice.Store<Assignment>().Saved);
    }

    /// <summary>
    /// A hand-dragged day can have two stops on top of each other — the manual path is an
    /// instruction and does not re-time the run around it — and a day like that is not a route.
    /// </summary>
    [Fact]
    public async Task RefusesToPlanAroundADayThatOverlapsItself()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);
        var morning = await ABookedJob(slice, Slough);
        var alsoMorning = await ABookedJob(slice, Croydon);

        // Both dropped on the same hour, which no van can do.
        await slice.Send(new AssignJobCommand(morning, technician, MondayMorning.AddHours(1)));
        await slice.Send(new AssignJobCommand(alsoMorning, technician, MondayMorning.AddHours(1)));

        var emergency = await ABookedJob(slice, Camden, JobPriority.Emergency);
        var inserted = await slice.Send(new InsertJobCommand(emergency));

        Assert.Equal(SchedulingErrors.OverlappingDayCode, inserted.Error!.Code);
        Assert.Equal(2, slice.Store<Assignment>().Saved.Count);
    }

    /// <summary>
    /// Work already being driven to is not schedulable, so it is not in the horizon's job list —
    /// but the van is still busy with it, and an engine that could not see the stop would plan the
    /// emergency straight over the top.
    /// </summary>
    [Fact]
    public async Task PlansAroundAStopWhoseJobIsAlreadyUnderWay()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);
        var underway = await ABookedJob(slice, Slough);
        await slice.Send(new AssignJobCommand(underway, technician, MondayMorning.AddHours(1)));

        foreach (var status in new[] { JobStatus.Dispatched, JobStatus.EnRoute })
        {
            await slice.Send(new ChangeJobStatusCommand(underway, status));
        }

        var emergency = await ABookedJob(slice, Camden, JobPriority.Emergency);
        var inserted = await slice.Send(new InsertJobCommand(emergency));

        Assert.True(inserted.IsSuccess);

        var day = slice.Store<Assignment>().Saved
            .Where(stop => stop.TechnicianId == technician)
            .OrderBy(stop => stop.ScheduledStart)
            .ToList();

        // Two stops, and the technician is in one place at a time: the emergency was planned
        // around the hour that was already spoken for, not on top of it.
        Assert.Equal(2, day.Count);
        Assert.True(day[1].ScheduledStart >= day[0].ScheduledStart + TimeSpan.FromHours(1));
    }

    /// <summary>
    /// A stop a dispatcher placed by hand keeps the time the customer was told, even when an
    /// emergency arrives in front of it.
    /// </summary>
    /// <remarks>
    /// The engine's own regression is <c>InsertionTests
    /// .LeavesAStopAtTheTimeItWasPromisedRatherThanPullingItForward</c>; this is the same claim
    /// through the rows, because it is the manual path that produces days with gaps in them and the
    /// stored plan that carries the promise. Without it, dropping in one emergency rewrote the
    /// whole of that technician's day — a two o'clock appointment answered at half past nine, and
    /// the phone told so.
    /// </remarks>
    [Fact]
    public async Task LeavesAHandPlacedStopAtTheTimeItWasPromised()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);

        var afternoon = await ABookedJob(slice, Slough);
        var promised = MondayMorning.AddHours(6);
        await slice.Send(new AssignJobCommand(afternoon, technician, promised));

        var emergency = await ABookedJob(slice, Camden, JobPriority.Emergency);
        var inserted = await slice.Send(new InsertJobCommand(emergency));

        Assert.True(inserted.IsSuccess);

        var day = slice.Store<Assignment>().Saved
            .Where(stop => stop.TechnicianId == technician)
            .ToList();

        Assert.Equal(promised, Assert.Single(day, stop => stop.JobId == afternoon).ScheduledStart);
        Assert.True(Assert.Single(day, stop => stop.JobId == emergency).ScheduledStart < promised);
    }

    /// <summary>
    /// The same, for the stop that must not move at all: one a technician is already driving to.
    /// </summary>
    [Fact]
    public async Task LeavesAStopBeingDrivenToWhereItWas()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);

        var underway = await ABookedJob(slice, Slough);
        var promised = MondayMorning.AddHours(6);
        await slice.Send(new AssignJobCommand(underway, technician, promised));

        foreach (var status in new[] { JobStatus.Dispatched, JobStatus.EnRoute })
        {
            await slice.Send(new ChangeJobStatusCommand(underway, status));
        }

        var emergency = await ABookedJob(slice, Camden, JobPriority.Emergency);
        await slice.Send(new InsertJobCommand(emergency));

        var stop = Assert.Single(slice.Store<Assignment>().Saved, saved => saved.JobId == underway);
        Assert.Equal(promised, stop.ScheduledStart);
    }

    [Fact]
    public async Task RefusesWorkThatCanNoLongerBePlanned()
    {
        await using var slice = SliceHost.Dispatching();
        await ATechnician(slice);
        var job = await ABookedJob(slice, Slough);
        await slice.Send(new ChangeJobStatusCommand(job, JobStatus.Cancelled));

        var inserted = await slice.Send(new InsertJobCommand(job));

        Assert.Equal(JobErrors.NotSchedulableCode, inserted.Error!.Code);
    }

    [Fact]
    public async Task RefusesAJobThisTenantDoesNotHave()
    {
        await using var slice = SliceHost.Dispatching();
        await ATechnician(slice);

        var inserted = await slice.Send(new InsertJobCommand(JobId.New()));

        Assert.Equal(JobErrors.NotFoundCode, inserted.Error!.Code);
    }

    /// <summary>The plan as a comparable shape: who goes where, when, in what order.</summary>
    private static IReadOnlyList<(JobId Job, TechnicianId Technician, DateTimeOffset Start, int Sequence)> Plan(
        SliceHost slice) =>
        [
            .. slice.Store<Assignment>().Saved
                .Select(stop => (stop.JobId, stop.TechnicianId, stop.ScheduledStart, stop.Sequence))
                .OrderBy(stop => stop.JobId.Value),
        ];

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
        JobPriority priority = JobPriority.Normal,
        string skill = "hvac")
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
            priority,
            Day.Start,
            Day.End,
            TimeSpan.FromHours(1)));

        return job.Value;
    }
}
