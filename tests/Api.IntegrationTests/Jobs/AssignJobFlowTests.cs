using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs.AssignJob;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Jobs;

/// <summary>
/// The manual dispatch path against a real database.
/// </summary>
/// <remarks>
/// The unit tests settle what the handler decides. What only Postgres can settle is that three
/// aggregates move in one transaction, and that dragging a job around never leaves two stops
/// behind it — a claim the unique index on <c>assignments(job_id)</c> would turn into a failed
/// insert if the handler ever added where it should have moved.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class AssignJobFlowTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public AssignJobFlowTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task PlansAJobThenHandsItToSomebodyElseAndStillHasOneStop()
    {
        await using var services = BuildHost();
        var job = await ABookedJobAsync(services);
        var sam = await ATechnicianAsync(services, "Sam Rivera");
        var ada = await ATechnicianAsync(services, "Ada Okafor");

        var planned = await Send(services, new AssignJobCommand(job, sam, MondayMorning.AddHours(1)));
        Assert.True(planned.IsSuccess);

        await using (var context = _postgres.NewContext(_tenant))
        {
            var stop = Assert.Single(await context.Assignments.ToListAsync());
            Assert.Equal(planned.Value, stop.Id);
            Assert.Equal(sam, stop.TechnicianId);
            Assert.Equal(MondayMorning.AddHours(1), stop.ScheduledStart);

            // Driven to from the home base, which the depot and the site being 30km apart makes a
            // number rather than a zero.
            Assert.True(stop.TravelMin > 0d);

            // One transaction: the plan and the demand agree about what has happened.
            var planned2 = await context.Jobs.SingleAsync(candidate => candidate.Id == job);
            Assert.Equal(JobStatus.Scheduled, planned2.Status);
        }

        var moved = await Send(services, new AssignJobCommand(job, ada, MondayMorning.AddHours(4)));
        Assert.Equal(planned.Value, moved.Value);

        await using (var context = _postgres.NewContext(_tenant))
        {
            var stop = Assert.Single(await context.Assignments.ToListAsync());
            Assert.Equal(ada, stop.TechnicianId);
            Assert.Equal(MondayMorning.AddHours(4), stop.ScheduledStart);

            // Still Scheduled: the job was planned once and has only been re-planned since.
            var reloaded = await context.Jobs.SingleAsync(candidate => candidate.Id == job);
            Assert.Equal(JobStatus.Scheduled, reloaded.Status);
        }
    }

    private async Task<JobId> ABookedJobAsync(ServiceProvider services)
    {
        var customer = await Send(services, new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value,
            "Head office",
            "12 Bath Road, Slough",
            51.5107,
            -0.5950));

        var job = await Send(services, new CreateJobCommand(
            customer.Value,
            location.Value,
            "hvac",
            JobPriority.Normal,
            MondayMorning,
            MondayMorning.AddHours(8),
            TimeSpan.FromHours(1)));

        return job.Value;
    }

    private async Task<TechnicianId> ATechnicianAsync(ServiceProvider services, string name)
    {
        var technician = await Send(services, new CreateTechnicianCommand(
            name,
            ["hvac"],
            MondayMorning,
            MondayMorning.AddHours(9),
            51.5074,
            -0.1278));

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
