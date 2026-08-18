using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Customers;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Customers.EraseCustomer;
using OpenDispatch.Application.Customers.ListCustomers;
using OpenDispatch.Application.Customers.RetireCustomer;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Scheduling.OptimizeDay;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Application.Technicians.ListTechnicians;
using OpenDispatch.Application.Technicians.RetireTechnician;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Customers;

/// <summary>
/// Somebody leaves the crew, or a customer moves away.
/// </summary>
/// <remarks>
/// <para>
/// The thing every shop needs within a month and this system had no answer for. There is no delete
/// — a technician's name is on every stop they drove and a customer's is on their invoices — so
/// "remove them" has to mean stop offering them while the history stays exactly where it is. These
/// assert both halves, because either one alone is the wrong feature: hiding without keeping is a
/// delete, and keeping without hiding is what was already there.
/// </para>
/// <para>
/// The scheduler is the assertion that matters most. A retired technician who still gets planned a
/// day is a retirement that did nothing.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class RetirementFlowTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public RetirementFlowTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task ARetiredTechnicianIsNoLongerPlannedADayButKeepsTheirHistory()
    {
        await using var services = BuildHost();

        var leaving = await Send(services, new CreateTechnicianCommand(
            "Sam Rivera", ["hvac"], MondayMorning, MondayMorning.AddHours(9), 51.5074d, -0.1278d));
        var staying = await Send(services, new CreateTechnicianCommand(
            "Ada Okafor", ["hvac"], MondayMorning, MondayMorning.AddHours(9), 51.5074d, -0.1278d));

        var job = await ABookedJobAsync(services);

        Assert.True((await Send(services, new RetireTechnicianCommand(leaving.Value, Retired: true))).IsSuccess);

        // Gone from the crew the office picks from…
        var crew = await Send(services, new ListTechniciansQuery());
        Assert.DoesNotContain(crew.Value, member => member.Id == leaving.Value);
        Assert.Contains(crew.Value, member => member.Id == staying.Value);

        // …and gone from the crew the optimiser considers, which is the half that matters.
        var planned = await Send(services, new OptimizeDayCommand(MondayMorning, MondayMorning.AddHours(9)));
        Assert.True(planned.IsSuccess);

        await using var context = _postgres.NewContext(_tenant);
        var stop = await context.Assignments.SingleAsync(row => row.JobId == job);

        Assert.Equal(staying.Value, stop.TechnicianId);

        // The record itself is untouched: they are still in the database, still named, still
        // resolvable by everything that points at them.
        var them = await context.Technicians.SingleAsync(row => row.Id == leaving.Value);

        Assert.Equal("Sam Rivera", them.Name);
        Assert.False(them.IsActive);
        Assert.NotNull(them.RetiredAt);
    }

    [Fact]
    public async Task ReinstatingPutsThemBackOnTheCrew()
    {
        await using var services = BuildHost();

        var technician = await Send(services, new CreateTechnicianCommand(
            "Sam Rivera", ["hvac"], MondayMorning, MondayMorning.AddHours(9), 51.5074d, -0.1278d));

        await Send(services, new RetireTechnicianCommand(technician.Value, Retired: true));
        await Send(services, new RetireTechnicianCommand(technician.Value, Retired: false));

        var crew = await Send(services, new ListTechniciansQuery());

        Assert.Contains(crew.Value, member => member.Id == technician.Value);
    }

    [Fact]
    public async Task ARetiredCustomerLeavesTheListAndKeepsTheirJobs()
    {
        await using var services = BuildHost();

        var moved = await Send(services, new CreateCustomerCommand("Gone Away Ltd", null, null));
        var staying = await Send(services, new CreateCustomerCommand("Still Here Ltd", null, null));

        var location = await Send(services, new AddServiceLocationCommand(
            moved.Value, "Site", "1 Mill Lane", 51.3762d, -0.0982d));
        var job = await Send(services, new CreateJobCommand(
            moved.Value, location.Value, "hvac", JobPriority.Normal,
            MondayMorning, MondayMorning.AddHours(4), TimeSpan.FromHours(1)));

        Assert.True((await Send(services, new RetireCustomerCommand(moved.Value, Retired: true))).IsSuccess);

        var listed = await Send(services, new ListCustomersQuery(1, 50));

        Assert.DoesNotContain(listed.Value.Items, row => row.Id == moved.Value);
        Assert.Contains(listed.Value.Items, row => row.Id == staying.Value);

        // The total is over the same set as the page, or a "next" button lands on nothing.
        Assert.Equal(listed.Value.Items.Count, listed.Value.Total);

        await using var context = _postgres.NewContext(_tenant);

        Assert.Equal("Gone Away Ltd", (await context.Customers.SingleAsync(row => row.Id == moved.Value)).Name);
        Assert.NotNull(await context.Jobs.SingleOrDefaultAsync(row => row.Id == job.Value));
    }

    /// <summary>
    /// Retiring and erasing are different acts, and the aggregate is what keeps them apart: an
    /// erased customer cannot be reinstated, because that would be editing somebody back into
    /// existence after they were told they were gone.
    /// </summary>
    [Fact]
    public async Task AnErasedCustomerCannotBeRetiredOrReinstated()
    {
        await using var services = BuildHost();

        var customer = await Send(services, new CreateCustomerCommand("Dana Whitlock", null, null));
        await Send(services, new EraseCustomerCommand(customer.Value));

        foreach (var direction in new[] { true, false })
        {
            var refused = await Send(services, new RetireCustomerCommand(customer.Value, direction));

            Assert.Equal(CustomerErrors.Erased(customer.Value), refused.Error);
        }
    }

    private async Task<JobId> ABookedJobAsync(ServiceProvider services)
    {
        var customer = await Send(services, new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value, "Head office", "12 Bath Road, Slough", 51.5107d, -0.5950d));

        var job = await Send(services, new CreateJobCommand(
            customer.Value, location.Value, "hvac", JobPriority.Normal,
            MondayMorning, MondayMorning.AddHours(4), TimeSpan.FromHours(1)));

        return job.Value;
    }

    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);

    private async Task<TResponse> Send<TResponse>(ServiceProvider services, IRequest<TResponse> request)
    {
        using var scope = services.ActingAs(_tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}
