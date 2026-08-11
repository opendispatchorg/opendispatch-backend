using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Scheduling;
using OpenDispatch.Application.Scheduling.InsertJob;
using OpenDispatch.Application.Scheduling.OptimizeDay;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Scheduling;

/// <summary>
/// Emergency dispatch against a real database.
/// </summary>
/// <remarks>
/// The insert is the one planning path that reads the plan back in order to change it, so this is
/// the test that the round trip closes: a day written by the optimiser, loaded out of Postgres,
/// turned back into something the engine can insert into, and written again with only the stops
/// that moved touched.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class InsertJobFlowTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeWindow Day = new(MondayMorning, MondayMorning.AddHours(9));

    private static readonly GeoPoint Depot = new(51.5074d, -0.1278d);
    private static readonly GeoPoint[] Sites =
    [
        new(51.5107d, -0.5950d),
        new(51.3762d, -0.0982d),
        new(51.5390d, -0.1426d),
        new(51.4816d, -0.0076d),
    ];

    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public InsertJobFlowTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task SlotsAnEmergencyIntoAPlannedDayAndTouchesNothingElse()
    {
        await using var services = BuildHost();
        await ATechnicianAsync(services, "Sam Rivera");
        await ATechnicianAsync(services, "Ada Okafor");

        foreach (var site in Sites)
        {
            await ABookedJobAsync(services, site);
        }

        Assert.True((await Send(services, new OptimizeDayCommand(Day.Start, Day.End))).IsSuccess);
        var before = await PlanAsync();

        var emergency = await ABookedJobAsync(services, new GeoPoint(51.4934d, 0.0098d), JobPriority.Emergency);
        var inserted = await Send(services, new InsertJobCommand(emergency));

        Assert.True(inserted.IsSuccess);

        var after = await PlanAsync();
        Assert.Equal(before.Count + 1, after.Count);

        var stop = Assert.Single(after, planned => planned.Job == emergency.Value);
        Assert.Equal(inserted.Value.TechnicianId.Value, stop.Technician);
        Assert.Equal(inserted.Value.ScheduledStart, stop.Start);

        // Every stop that changed is on the technician who took the emergency, and there are as
        // many of them as the answer reported.
        var moved = before.Where(planned => !after.Contains(planned)).ToList();
        Assert.All(moved, planned => Assert.Equal(inserted.Value.TechnicianId.Value, planned.Technician));
        Assert.Equal(inserted.Value.Displaced, moved.Count);

        Assert.Equal(
            JobStatus.Scheduled,
            await StatusAsync(emergency));
    }

    [Fact]
    public async Task RefusesAnEmergencyNobodyCanTakeAndLeavesTheBoardAsItWas()
    {
        await using var services = BuildHost();
        await ATechnicianAsync(services, "Sam Rivera");
        await ABookedJobAsync(services, Sites[0]);
        await Send(services, new OptimizeDayCommand(Day.Start, Day.End));

        var before = await PlanAsync();
        var gas = await ABookedJobAsync(services, Sites[1], skill: "gas safe");

        var inserted = await Send(services, new InsertJobCommand(gas));

        Assert.Equal(SchedulingErrors.CouldNotPlaceCode, inserted.Error!.Code);

        // The failure rolled the transaction back, so the day is untouched and the job is still
        // only demand.
        Assert.Equal(before, await PlanAsync());
        Assert.Equal(JobStatus.Unscheduled, await StatusAsync(gas));
    }

    /// <summary>The plan as a comparable shape, read back out of the database.</summary>
    private async Task<IReadOnlyList<(Guid Job, Guid Technician, DateTimeOffset Start, int Sequence)>> PlanAsync()
    {
        await using var context = _postgres.NewContext(_tenant);

        return await context.Assignments
            .OrderBy(assignment => assignment.ScheduledStart)
            .ThenBy(assignment => assignment.Id)
            .Select(assignment => new ValueTuple<Guid, Guid, DateTimeOffset, int>(
                assignment.JobId.Value,
                assignment.TechnicianId.Value,
                assignment.ScheduledStart,
                assignment.Sequence))
            .ToListAsync();
    }

    private async Task<JobStatus> StatusAsync(JobId job)
    {
        await using var context = _postgres.NewContext(_tenant);

        return await context.Jobs.Where(candidate => candidate.Id == job).Select(candidate => candidate.Status).SingleAsync();
    }

    private async Task<JobId> ABookedJobAsync(
        ServiceProvider services,
        GeoPoint where,
        JobPriority priority = JobPriority.Normal,
        string skill = "hvac")
    {
        var customer = await Send(services, new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value,
            "Site",
            "12 Bath Road",
            where.Lat,
            where.Lng));

        var job = await Send(services, new CreateJobCommand(
            customer.Value,
            location.Value,
            skill,
            priority,
            Day.Start,
            Day.End,
            TimeSpan.FromHours(1)));

        return job.Value;
    }

    private async Task<TechnicianId> ATechnicianAsync(ServiceProvider services, string name)
    {
        var technician = await Send(services, new CreateTechnicianCommand(
            name,
            ["hvac"],
            Day.Start,
            Day.End,
            Depot.Lat,
            Depot.Lng));

        return technician.Value;
    }

    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);

    /// <summary>One request: one scope, one tenant, one unit of work.</summary>
    private async Task<TResponse> Send<TResponse>(ServiceProvider services, IRequest<TResponse> request)
    {
        using var scope = services.ActingAs(_tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}
