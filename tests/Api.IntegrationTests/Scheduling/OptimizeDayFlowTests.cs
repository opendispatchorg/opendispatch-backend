using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Scheduling.OptimizeDay;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Scheduling;

/// <summary>
/// The optimiser over persisted data: rows in, a solved day, rows back out.
/// </summary>
/// <remarks>
/// This is the step's "done when" and the first time the engine has ever seen data that came out
/// of Postgres. What only a database can settle is that the plan it produces survives being
/// written — every stop, its sequence and its drive — and that running it again over the same rows
/// rewrites them in place rather than accumulating a second day.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class OptimizeDayFlowTests
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

    public OptimizeDayFlowTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task PlansASeededDayAndPlansItTheSameWayTheSecondTime()
    {
        await using var services = BuildHost();
        await ATechnicianAsync(services, "Sam Rivera");
        await ATechnicianAsync(services, "Ada Okafor");

        foreach (var site in Sites)
        {
            await ABookedJobAsync(services, site);
        }

        var first = await Send(services, new OptimizeDayCommand(Day.Start, Day.End));

        Assert.True(first.IsSuccess);
        Assert.Equal(Sites.Length, first.Value.Planned);
        Assert.Empty(first.Value.Unassigned);

        var afterFirst = await PlanAsync();

        // Everything the engine decided is on the rows, not just the count.
        Assert.All(afterFirst, stop => Assert.True(stop.TravelMin >= 0d));
        Assert.All(afterFirst, stop => Assert.True(stop.Start >= Day.Start));
        Assert.All(await StatusesAsync(), status => Assert.Equal(JobStatus.Scheduled, status));

        var second = await Send(services, new OptimizeDayCommand(Day.Start, Day.End));
        var afterSecond = await PlanAsync();

        // Rewritten in place: four stops, not eight. That the plan is also reproducible is pinned
        // against a day big enough for the seed to matter in the unit tests; here it is the
        // rewriting that only a database can show.
        Assert.Equal(afterFirst, afterSecond);
        Assert.Equal(first.Value.Cost, second.Value.Cost);
        Assert.Equal(Sites.Length, afterSecond.Count);
    }

    /// <summary>The plan as a comparable shape, read back out of the database.</summary>
    private async Task<IReadOnlyList<(Guid Job, Guid Technician, DateTimeOffset Start, int Sequence, double TravelMin)>>
        PlanAsync()
    {
        await using var context = _postgres.NewContext(_tenant);

        return await context.Assignments
            .OrderBy(assignment => assignment.ScheduledStart)
            .ThenBy(assignment => assignment.Id)
            .Select(assignment => new ValueTuple<Guid, Guid, DateTimeOffset, int, double>(
                assignment.JobId.Value,
                assignment.TechnicianId.Value,
                assignment.ScheduledStart,
                assignment.Sequence,
                assignment.TravelMin))
            .ToListAsync();
    }

    private async Task<IReadOnlyList<JobStatus>> StatusesAsync()
    {
        await using var context = _postgres.NewContext(_tenant);

        return await context.Jobs.Select(job => job.Status).ToListAsync();
    }

    private async Task<JobId> ABookedJobAsync(ServiceProvider services, GeoPoint where)
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
            "hvac",
            JobPriority.Normal,
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
