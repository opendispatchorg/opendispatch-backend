using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Jobs;

/// <summary>
/// Booking a job against a real database, through the registrations the host uses.
/// </summary>
/// <remarks>
/// This is the first flow that crosses two aggregates — a customer is read so that a job can be
/// written — and the first job row the system has ever inserted, so it is also the first proof
/// that the status enum, the window columns the step-27 composite index is built on, and the
/// location the scheduler will read all survive the trip.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class CreateJobFlowTests
{
    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public CreateJobFlowTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task TakesInAJobAgainstACustomersSiteAndWritesItUnscheduled()
    {
        await using var services = BuildHost();

        var customer = await Send(services, new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value,
            "Head office",
            "12 Bath Road, Slough",
            51.5107,
            -0.5950));

        // Given in the shop's own summer offset, which Npgsql would refuse against timestamptz.
        var summerMorning = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.FromHours(2));

        var created = await Send(services, new CreateJobCommand(
            customer.Value,
            location.Value,
            "hvac",
            JobPriority.High,
            summerMorning,
            summerMorning.AddHours(3),
            TimeSpan.FromHours(2)));
        Assert.True(created.IsSuccess);

        await using var context = _postgres.NewContext(_tenant);
        var job = Assert.Single(await context.Jobs.ToListAsync());

        Assert.Equal(created.Value, job.Id);
        Assert.Equal(customer.Value, job.CustomerId);
        Assert.Equal(location.Value, job.LocationId);
        Assert.Equal(JobStatus.Unscheduled, job.Status);
        Assert.Equal(JobPriority.High, job.Priority);
        Assert.Equal("hvac", job.RequiredSkill);
        Assert.Equal(TimeSpan.FromHours(2), job.EstimatedDuration);

        Assert.Equal(summerMorning, job.Window.Start);
        Assert.Equal(TimeSpan.Zero, job.Window.Start.Offset);
        Assert.Equal(TimeSpan.FromHours(3), job.Window.Duration);

        // The point came off the customer's service location and back out of PostGIS unchanged.
        Assert.Equal(51.5107, job.Location.Lat);
        Assert.Equal(-0.5950, job.Location.Lng);
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
