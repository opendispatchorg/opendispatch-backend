using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Dispatch.GetBoard;
using OpenDispatch.Application.Jobs.AssignJob;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Results;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Dispatch;

/// <summary>
/// The board, which is the one read path in the system that is a query rather than a repository.
/// </summary>
/// <remarks>
/// It can only be tested against a real database, because everything worth testing about it is
/// something SQL does: joining a job to the customer whose name goes on the block and to the site
/// whose address goes under it, without either being reachable from <c>Job</c>; and being scoped to
/// one tenant by a filter nothing in the query mentions.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class DispatchBoardTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeWindow Day = new(MondayMorning, MondayMorning.AddHours(9));

    private static readonly GeoPoint Depot = new(51.5074d, -0.1278d);
    private static readonly GeoPoint Slough = new(51.5107d, -0.5950d);
    private static readonly GeoPoint Croydon = new(51.3762d, -0.0982d);

    private readonly OrgId _tenant = OrgId.New();
    private readonly OrgId _rival = OrgId.New();
    private readonly PostgresFixture _postgres;

    public DispatchBoardTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task DrawsEveryLaneWithItsStopsAndThePileNobodyIsGoingTo()
    {
        await using var services = BuildHost();
        var sam = await ATechnicianAsync(services, "Sam Rivera");
        var ada = await ATechnicianAsync(services, "Ada Okafor");

        var morning = await ABookedJobAsync(services, Slough, "Vance Refrigeration", "12 Bath Road, Slough");
        var afternoon = await ABookedJobAsync(services, Croydon, "Ivy Fabrication", "3 Mill Lane, Croydon");
        var nobodyCanTake = await ABookedJobAsync(services, Slough, "Riverside Heating", "9 Wharf Road", skill: "gas safe");

        await Send(services, new AssignJobCommand(morning, sam, MondayMorning.AddHours(1)));
        await Send(services, new AssignJobCommand(afternoon, sam, MondayMorning.AddHours(5)));

        var board = await Board(services);

        Assert.Equal(Day, board.Day);

        // A lane per technician, by name, and one with nothing on is still a lane.
        Assert.Equal(["Ada Okafor", "Sam Rivera"], board.Routes.Select(route => route.Name));
        Assert.Empty(Assert.Single(board.Routes, route => route.TechnicianId == ada).Stops);

        var lane = Assert.Single(board.Routes, route => route.TechnicianId == sam);
        Assert.Equal(Day, lane.Shift);
        Assert.Equal(Depot, lane.HomeBase);
        Assert.Equal(["hvac"], lane.Skills);
        Assert.Equal([morning, afternoon], lane.Stops.Select(stop => stop.Job.JobId));
        Assert.Equal(
            [MondayMorning.AddHours(1), MondayMorning.AddHours(5)],
            lane.Stops.Select(stop => stop.ScheduledStart));

        // The two fields the projection exists for: neither is on Job, and neither is reachable
        // from it without a join.
        var first = lane.Stops[0].Job;
        Assert.Equal("Vance Refrigeration", first.CustomerName);
        Assert.Equal("12 Bath Road, Slough", first.Address);
        Assert.Equal("Ivy Fabrication", lane.Stops[1].Job.CustomerName);
        Assert.Equal("3 Mill Lane, Croydon", lane.Stops[1].Job.Address);

        // Everything else a block is drawn from came back too.
        Assert.Equal(JobStatus.Scheduled, first.Status);
        Assert.Equal(JobPriority.Normal, first.Priority);
        Assert.Equal("hvac", first.RequiredSkill);
        Assert.Equal(TimeSpan.FromHours(1), first.EstimatedDuration);
        Assert.Equal(Slough, first.Location);
        Assert.Equal(Day, first.Window);

        // And the job nobody was given is in the pile rather than missing from the board.
        var waiting = Assert.Single(board.Unassigned);
        Assert.Equal(nobodyCanTake, waiting.JobId);
        Assert.Equal("gas safe", waiting.RequiredSkill);
        Assert.Equal("Riverside Heating", waiting.CustomerName);
        Assert.Equal(JobStatus.Unscheduled, waiting.Status);
    }

    /// <summary>
    /// Two organizations with real days of their own, drawn through one read model that never
    /// mentions a tenant: the filter is on the model, not in the query.
    /// </summary>
    /// <remarks>
    /// Both boards are populated on purpose. A rival with no crew would have nowhere to draw
    /// another tenant's stops even if it read them, so the test would pass on a query that leaked —
    /// which is exactly what it is here to rule out.
    /// </remarks>
    [Fact]
    public async Task ShowsOneTenantNothingOfAnothersDay()
    {
        await using var services = BuildHost();

        var sam = await ATechnicianAsync(services, "Sam Rivera");
        var mineJob = await ABookedJobAsync(services, Slough, "Vance Refrigeration", "12 Bath Road, Slough");
        await Send(services, new AssignJobCommand(mineJob, sam, MondayMorning.AddHours(1)));
        var mineWaiting = await ABookedJobAsync(services, Croydon, "Ivy Fabrication", "3 Mill Lane");

        var rivalTech = await ATechnicianAsync(services, "Jo Bennett", _rival);
        var rivalJob = await ABookedJobAsync(services, Croydon, "Riverside Heating", "9 Wharf Road", actingAs: _rival);
        await Send(services, new AssignJobCommand(rivalJob, rivalTech, MondayMorning.AddHours(3)), _rival);

        var mine = await Board(services);
        var theirs = await Board(services, _rival);

        // Each board is a whole day, and neither contains a single row of the other's.
        Assert.Equal([sam], mine.Routes.Select(route => route.TechnicianId));
        Assert.Equal([mineJob], Assert.Single(mine.Routes).Stops.Select(stop => stop.Job.JobId));
        Assert.Equal([mineWaiting], mine.Unassigned.Select(job => job.JobId));

        Assert.Equal([rivalTech], theirs.Routes.Select(route => route.TechnicianId));
        Assert.Equal([rivalJob], Assert.Single(theirs.Routes).Stops.Select(stop => stop.Job.JobId));
        Assert.Empty(theirs.Unassigned);
    }

    /// <summary>
    /// Work that has been called off is not waiting to be planned, and work that is being driven to
    /// is on a lane. Neither belongs in the pile.
    /// </summary>
    [Fact]
    public async Task KeepsCancelledWorkOutOfThePileAndPlannedWorkOnItsLane()
    {
        await using var services = BuildHost();
        var sam = await ATechnicianAsync(services, "Sam Rivera");

        var cancelled = await ABookedJobAsync(services, Slough, "Vance Refrigeration", "12 Bath Road");
        await Send(services, new ChangeJobStatusCommand(cancelled, JobStatus.Cancelled));

        var underway = await ABookedJobAsync(services, Croydon, "Ivy Fabrication", "3 Mill Lane");
        await Send(services, new AssignJobCommand(underway, sam, MondayMorning.AddHours(2)));

        foreach (var status in new[] { JobStatus.Dispatched, JobStatus.EnRoute })
        {
            await Send(services, new ChangeJobStatusCommand(underway, status));
        }

        var board = await Board(services);

        Assert.Empty(board.Unassigned);

        var stop = Assert.Single(Assert.Single(board.Routes).Stops);
        Assert.Equal(underway, stop.Job.JobId);
        Assert.Equal(JobStatus.EnRoute, stop.Job.Status);
    }

    /// <summary>
    /// Step 36 lets a dispatcher put work on somebody who does not hold its skill — the engine
    /// never will, but overruling it is deliberate — and said the board is where that becomes
    /// visible. This is that: the lane carries what the technician can do, every block carries what
    /// it needs, and the two can be compared.
    /// </summary>
    [Fact]
    public async Task CarriesBothHalvesOfASkillMismatchSoTheBoardCanShowOne()
    {
        await using var services = BuildHost();
        var plumber = await ATechnicianAsync(services, "Ada Okafor", skill: "plumbing");
        var hvac = await ABookedJobAsync(services, Slough, "Vance Refrigeration", "12 Bath Road");

        Assert.True((await Send(services, new AssignJobCommand(hvac, plumber, MondayMorning.AddHours(1)))).IsSuccess);

        var lane = Assert.Single((await Board(services)).Routes);
        var stop = Assert.Single(lane.Stops);

        Assert.Equal(["plumbing"], lane.Skills);
        Assert.Equal("hvac", stop.Job.RequiredSkill);
        Assert.DoesNotContain(stop.Job.RequiredSkill, lane.Skills, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShowsAnEmptyBoardForADayWithNothingOnIt()
    {
        await using var services = BuildHost();
        await ATechnicianAsync(services, "Sam Rivera");
        await ABookedJobAsync(services, Slough, "Vance Refrigeration", "12 Bath Road");

        var board = await Board(services, day: new TimeWindow(Day.Start.AddDays(7), Day.End.AddDays(7)));

        // The crew still has lanes — they are the board's rows — and nothing is on them.
        Assert.Empty(Assert.Single(board.Routes).Stops);
        Assert.Empty(board.Unassigned);
    }

    /// <summary>
    /// The claim the read model exists for: it asks the database a fixed number of questions,
    /// whatever the day holds.
    /// </summary>
    /// <remarks>
    /// A projection that quietly became a query per stop would return exactly the same board, so
    /// nothing but the round trips can tell the difference. Three: the crew, the stops with their
    /// jobs and customers, and the pile.
    /// </remarks>
    [Fact]
    public async Task AsksTheDatabaseTheSameThreeQuestionsHoweverBusyTheDayIs()
    {
        var counter = new RoundTripCounter();
        await using var services = BuildHost(counter);

        var sam = await ATechnicianAsync(services, "Sam Rivera");
        await ATechnicianAsync(services, "Ada Okafor");

        for (var stop = 0; stop < 6; stop++)
        {
            var job = await ABookedJobAsync(services, Slough, $"Customer {stop}", $"{stop} Bath Road");
            await Send(services, new AssignJobCommand(job, sam, MondayMorning.AddHours(stop + 1)));
        }

        await ABookedJobAsync(services, Croydon, "Waiting", "9 Wharf Road", skill: "gas safe");

        var before = counter.Executed;
        var board = await Board(services);
        var asked = counter.Executed - before;

        Assert.Equal(6, Assert.Single(board.Routes, route => route.TechnicianId == sam).Stops.Count);
        Assert.Single(board.Unassigned);
        Assert.Equal(3, asked);
    }

    [Fact]
    public async Task RefusesADayThatEndsBeforeItStarts()
    {
        await using var services = BuildHost();

        var board = await Send(services, new GetBoardQuery(Day.End, Day.Start));

        var failure = Assert.IsType<ValidationError>(board.Error);
        Assert.Equal(
            "A day cannot end before it starts.",
            Assert.Single(failure.Failures[nameof(GetBoardQuery.To)]));
    }

    private async Task<Application.Abstractions.DispatchBoard> Board(
        ServiceProvider services,
        OrgId? actingAs = null,
        TimeWindow? day = null)
    {
        var window = day ?? Day;
        var board = await Send(services, new GetBoardQuery(window.Start, window.End), actingAs);

        Assert.True(board.IsSuccess);

        return board.Value;
    }

    private async Task<JobId> ABookedJobAsync(
        ServiceProvider services,
        GeoPoint where,
        string customerName,
        string address,
        string skill = "hvac",
        OrgId? actingAs = null)
    {
        var customer = await Send(services, new CreateCustomerCommand(customerName, null, null), actingAs);
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value,
            "Site",
            address,
            where.Lat,
            where.Lng), actingAs);

        var job = await Send(services, new CreateJobCommand(
            customer.Value,
            location.Value,
            skill,
            JobPriority.Normal,
            Day.Start,
            Day.End,
            TimeSpan.FromHours(1)), actingAs);

        return job.Value;
    }

    private async Task<TechnicianId> ATechnicianAsync(
        ServiceProvider services,
        string name,
        OrgId? actingAs = null,
        string skill = "hvac")
    {
        var technician = await Send(services, new CreateTechnicianCommand(
            name,
            [skill],
            Day.Start,
            Day.End,
            Depot.Lat,
            Depot.Lng), actingAs);

        return technician.Value;
    }

    private ServiceProvider BuildHost(RoundTripCounter? counter = null) =>
        TestHost.Over(_postgres)
            .AddLogging(logging =>
            {
                if (counter is not null)
                {
                    logging.SetMinimumLevel(LogLevel.Information).AddProvider(counter);
                }
            })
            .BuildServiceProvider(validateScopes: true);

    /// <summary>One request: one scope, one tenant, one unit of work.</summary>
    private async Task<TResponse> Send<TResponse>(
        ServiceProvider services,
        IRequest<TResponse> request,
        OrgId? actingAs = null)
    {
        using var scope = services.ActingAs(actingAs ?? _tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}
