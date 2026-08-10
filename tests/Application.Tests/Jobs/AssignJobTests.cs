using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Jobs.AssignJob;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Results;
using OpenDispatch.Application.Technicians;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling.Travel;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Jobs;

/// <summary>
/// The manual dispatch path — the board's drag — sent through the real pipeline.
/// </summary>
/// <remarks>
/// Three aggregates move together here, so what is worth testing is what only shows when they do:
/// that a job gets exactly one stop however many times it is dragged, that the drive is measured
/// from the right place, and that planning a job is the one thing that makes demand into a plan.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class AssignJobTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Where the crew starts the day, and two sites a long way from it and from each other.</summary>
    private static readonly GeoPoint Depot = new(51.5074d, -0.1278d);
    private static readonly GeoPoint Slough = new(51.5107d, -0.5950d);
    private static readonly GeoPoint Croydon = new(51.3762d, -0.0982d);

    private static readonly HaversineTravelTimeProvider Travel = new();

    [Fact]
    public async Task PlansAJobIntoATechniciansDayAndTurnsDemandIntoAPlan()
    {
        await using var slice = SliceHost.Dispatching();
        var job = await ABookedJob(slice, Slough);
        var technician = await ATechnician(slice);

        var assigned = await slice.Send(
            new AssignJobCommand(job, technician, MondayMorning.AddHours(1)));

        Assert.True(assigned.IsSuccess);

        var stop = Assert.Single(slice.Store<Assignment>().Saved);
        Assert.Equal(assigned.Value, stop.Id);
        Assert.Equal(slice.Tenant, stop.OrgId);
        Assert.Equal(job, stop.JobId);
        Assert.Equal(technician, stop.TechnicianId);
        Assert.Equal(MondayMorning.AddHours(1), stop.ScheduledStart);
        Assert.Equal(0, stop.Sequence);

        // The first stop of a day is driven to from the technician's home base, which is where the
        // engine measures it from too.
        Assert.Equal(Travel.Minutes(Depot, Slough), stop.TravelMin);

        Assert.Equal(JobStatus.Scheduled, Assert.Single(slice.Store<Job>().Saved).Status);
    }

    [Fact]
    public async Task MeasuresTheDriveToASecondStopFromTheOneBeforeIt()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);
        var morning = await ABookedJob(slice, Slough);
        var afternoon = await ABookedJob(slice, Croydon);

        await slice.Send(new AssignJobCommand(morning, technician, MondayMorning.AddHours(1)));
        await slice.Send(new AssignJobCommand(afternoon, technician, MondayMorning.AddHours(5)));

        var second = StopFor(slice, afternoon);
        Assert.Equal(1, second.Sequence);
        Assert.Equal(Travel.Minutes(Slough, Croydon), second.TravelMin);

        // Not the home base, which is the mistake this is here to catch: the two answers differ.
        Assert.NotEqual(Travel.Minutes(Depot, Croydon), second.TravelMin);
    }

    /// <summary>
    /// The step's own requirement, and the rule the whole plan rests on: a job is planned once.
    /// </summary>
    [Fact]
    public async Task HandingAStopToAnotherTechnicianMovesItRatherThanAddingASecond()
    {
        await using var slice = SliceHost.Dispatching();
        var job = await ABookedJob(slice, Slough);
        var first = await ATechnician(slice, "Sam Rivera");
        var second = await ATechnician(slice, "Ada Okafor");

        var planned = await slice.Send(new AssignJobCommand(job, first, MondayMorning.AddHours(1)));
        var moved = await slice.Send(new AssignJobCommand(job, second, MondayMorning.AddHours(3)));

        Assert.Equal(planned.Value, moved.Value);

        var stop = Assert.Single(slice.Store<Assignment>().Saved);
        Assert.Equal(second, stop.TechnicianId);
        Assert.Equal(MondayMorning.AddHours(3), stop.ScheduledStart);

        // Two events for one drag: the stop changed hands, and then it changed time. Step 8 chose
        // that, and the board is told both things.
        Assert.Equal(
            [typeof(AssignmentChanged), typeof(AssignmentChanged)],
            stop.DomainEvents.Select(raised => raised.GetType()));
    }

    [Fact]
    public async Task MovingAStopWithinTheSameDayKeepsItWhereItIsInTheRun()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);
        var job = await ABookedJob(slice, Slough);

        var planned = await slice.Send(new AssignJobCommand(job, technician, MondayMorning.AddHours(1)));
        var moved = await slice.Send(new AssignJobCommand(job, technician, MondayMorning.AddHours(4)));

        Assert.Equal(planned.Value, moved.Value);

        var stop = Assert.Single(slice.Store<Assignment>().Saved);
        Assert.Equal(MondayMorning.AddHours(4), stop.ScheduledStart);
        Assert.Equal(0, stop.Sequence);

        // Its own earlier placement is not its predecessor: the drive is still from the depot.
        Assert.Equal(Travel.Minutes(Depot, Slough), stop.TravelMin);
    }

    [Fact]
    public async Task AStopDroppedEarlierInTheDayTakesTheEarlierPositionInTheRun()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);
        var afternoon = await ABookedJob(slice, Croydon);
        var morning = await ABookedJob(slice, Slough);

        await slice.Send(new AssignJobCommand(afternoon, technician, MondayMorning.AddHours(5)));
        await slice.Send(new AssignJobCommand(morning, technician, MondayMorning.AddHours(1)));

        Assert.Equal(0, StopFor(slice, morning).Sequence);
        Assert.Equal(Travel.Minutes(Depot, Slough), StopFor(slice, morning).TravelMin);
    }

    /// <summary>
    /// Planning a job that is already planned moves it; planning one that is already being driven
    /// to does not, because that day belongs to the technician doing it.
    /// </summary>
    [Theory]
    [InlineData(JobStatus.EnRoute)]
    [InlineData(JobStatus.InProgress)]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Cancelled)]
    public async Task RefusesWorkThatCanNoLongerBePlanned(JobStatus status)
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);
        var job = await ABookedJob(slice, Slough);
        await DriveTo(slice, job, status);

        var assigned = await slice.Send(new AssignJobCommand(job, technician, MondayMorning.AddHours(1)));

        Assert.Equal(JobErrors.NotSchedulableCode, assigned.Error!.Code);
        Assert.Equal(ErrorCategory.Conflict, assigned.Error.Category);
        Assert.Empty(slice.Store<Assignment>().Saved);
    }

    /// <summary>
    /// A job already on a phone can still be moved — nobody has set off — and moving it must not
    /// push it round the state machine.
    /// </summary>
    [Fact]
    public async Task MovesADispatchedJobWithoutChangingItsStatus()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);
        var job = await ABookedJob(slice, Slough);
        await slice.Send(new AssignJobCommand(job, technician, MondayMorning.AddHours(1)));
        await slice.Send(new ChangeJobStatusCommand(job, JobStatus.Dispatched));

        var moved = await slice.Send(new AssignJobCommand(job, technician, MondayMorning.AddHours(2)));

        Assert.True(moved.IsSuccess);
        Assert.Equal(JobStatus.Dispatched, Assert.Single(slice.Store<Job>().Saved).Status);
        Assert.Equal(MondayMorning.AddHours(2), Assert.Single(slice.Store<Assignment>().Saved).ScheduledStart);
    }

    /// <summary>
    /// A skill the technician does not hold is a hard constraint for the <em>optimiser</em>, not a
    /// veto on a dispatcher. Refusing here would leave somebody who knows better with no way to
    /// say so — the same argument the architecture makes about lateness.
    /// </summary>
    [Fact]
    public async Task LetsADispatcherOverruleTheSkillTheEngineWouldInsistOn()
    {
        await using var slice = SliceHost.Dispatching();
        var plumber = await ATechnician(slice, "Ada Okafor", "plumbing");
        var job = await ABookedJob(slice, Slough);

        var assigned = await slice.Send(new AssignJobCommand(job, plumber, MondayMorning.AddHours(1)));

        Assert.True(assigned.IsSuccess);
        Assert.Equal(plumber, Assert.Single(slice.Store<Assignment>().Saved).TechnicianId);
    }

    [Fact]
    public async Task RefusesAJobThisTenantDoesNotHave()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);

        var assigned = await slice.Send(
            new AssignJobCommand(JobId.New(), technician, MondayMorning.AddHours(1)));

        Assert.Equal(JobErrors.NotFoundCode, assigned.Error!.Code);
    }

    [Fact]
    public async Task RefusesATechnicianThisTenantDoesNotHave()
    {
        await using var slice = SliceHost.Dispatching();
        var job = await ABookedJob(slice, Slough);

        var assigned = await slice.Send(
            new AssignJobCommand(job, TechnicianId.New(), MondayMorning.AddHours(1)));

        Assert.Equal(TechnicianErrors.NotFoundCode, assigned.Error!.Code);
        Assert.Empty(slice.Store<Assignment>().Saved);
    }

    [Fact]
    public async Task AStartGivenInAnotherOffsetIsKeptAsTheSameInstant()
    {
        await using var slice = SliceHost.Dispatching();
        var technician = await ATechnician(slice);
        var job = await ABookedJob(slice, Slough);
        var summerMorning = new DateTimeOffset(2026, 8, 10, 11, 0, 0, TimeSpan.FromHours(2));

        await slice.Send(new AssignJobCommand(job, technician, summerMorning));

        var stop = Assert.Single(slice.Store<Assignment>().Saved);
        Assert.Equal(summerMorning, stop.ScheduledStart);
        Assert.Equal(TimeSpan.Zero, stop.ScheduledStart.Offset);
    }

    private static Assignment StopFor(SliceHost slice, JobId job) =>
        Assert.Single(slice.Store<Assignment>().Saved, stop => stop.JobId == job);

    private static async Task DriveTo(SliceHost slice, JobId job, JobStatus status)
    {
        JobStatus[] walk = status is JobStatus.Cancelled
            ? [JobStatus.Cancelled]
            : [JobStatus.Scheduled, JobStatus.Dispatched, JobStatus.EnRoute, JobStatus.InProgress, JobStatus.Completed];

        foreach (var step in walk)
        {
            await slice.Send(new ChangeJobStatusCommand(job, step));

            if (step == status)
            {
                return;
            }
        }
    }

    private static async Task<TechnicianId> ATechnician(
        SliceHost slice,
        string name = "Sam Rivera",
        string skill = "hvac")
    {
        var technician = await slice.Send(new CreateTechnicianCommand(
            name,
            [skill],
            MondayMorning,
            MondayMorning.AddHours(9),
            Depot.Lat,
            Depot.Lng));

        return technician.Value;
    }

    private static async Task<JobId> ABookedJob(SliceHost slice, GeoPoint where)
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
            "hvac",
            JobPriority.Normal,
            MondayMorning,
            MondayMorning.AddHours(8),
            TimeSpan.FromHours(1)));

        return job.Value;
    }
}
