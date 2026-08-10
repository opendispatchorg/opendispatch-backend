using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Persistence;

/// <summary>
/// The persistence ports over a real database, resolved from the real registration.
/// </summary>
/// <remarks>
/// Repositories are covered incidentally rather than method by method (TESTING.md): one flow
/// stages every aggregate and reads them all back, and the rest of the tests are the queries
/// whose behaviour is a decision — which jobs the optimiser may move, what "in the horizon"
/// means at its edges, and whether a failed save takes everything with it.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class RepositoryTests
{
    private static readonly DateTimeOffset Morning = new(2027, 3, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeWindow Day = new(Morning, Morning.AddHours(10));

    private readonly PostgresFixture _postgres;

    public RepositoryTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>
    /// One save through one unit of work, five aggregates, then all of them read back through
    /// their own ports — including the collections their roots own, which nothing asked for.
    /// </summary>
    [Fact]
    public async Task RoundTripsEveryAggregateThroughItsRepository()
    {
        var customer = CustomerBuilder.Any().Named("Ivy Fabrication").Build();
        var location = customer.AddLocation("Works", "9 Foundry Lane", new GeoPoint(51.5080, -0.1281));
        var job = JobBuilder.Any().ForCustomer(customer.Id).Build();
        var technician = TechnicianBuilder.Any().Named("Ada").Skilled("HVAC").Build();
        var assignment = AssignmentBuilder.Any().ForJob(job.Id).ForTechnician(technician.Id).Build();
        var invoice = InvoiceBuilder.Any().ForJob(job.Id).Build();
        invoice.AddLineItem(LineItemKind.Labor, "Two hours on site", 2m, Money.FromDollars(90m));

        using (var scope = _postgres.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<ICustomerRepository>().Add(customer);
            scope.ServiceProvider.GetRequiredService<IJobRepository>().Add(job);
            scope.ServiceProvider.GetRequiredService<ITechnicianRepository>().Add(technician);
            scope.ServiceProvider.GetRequiredService<IAssignmentRepository>().Add(assignment);
            scope.ServiceProvider.GetRequiredService<IInvoiceRepository>().Add(invoice);

            var written = await scope.ServiceProvider
                .GetRequiredService<IUnitOfWork>()
                .SaveChangesAsync(CancellationToken.None);

            Assert.True(written > 0);
        }

        using var read = _postgres.CreateScope();

        var loadedCustomer = await read.ServiceProvider
            .GetRequiredService<ICustomerRepository>()
            .GetAsync(customer.Id, CancellationToken.None);
        var loadedJob = await read.ServiceProvider
            .GetRequiredService<IJobRepository>()
            .GetAsync(job.Id, CancellationToken.None);
        var loadedTechnician = await read.ServiceProvider
            .GetRequiredService<ITechnicianRepository>()
            .GetAsync(technician.Id, CancellationToken.None);
        var loadedAssignment = await read.ServiceProvider
            .GetRequiredService<IAssignmentRepository>()
            .GetAsync(assignment.Id, CancellationToken.None);
        var loadedInvoice = await read.ServiceProvider
            .GetRequiredService<IInvoiceRepository>()
            .GetAsync(invoice.Id, CancellationToken.None);

        Assert.Equal("Ivy Fabrication", loadedCustomer?.Name);
        Assert.Equal(location, Assert.Single(loadedCustomer!.Locations).Id);
        Assert.Equal(job.RequiredSkill, loadedJob?.RequiredSkill);
        Assert.True(loadedTechnician?.HasSkill("hvac"));
        Assert.Equal(job.Id, loadedAssignment?.JobId);
        Assert.Equal(18_000L, Assert.Single(loadedInvoice!.Lines).LineTotal.Cents);
    }

    /// <summary>
    /// Which work the optimiser may move is a domain rule, and this is where it meets SQL. Both
    /// halves are asserted: a job past <see cref="JobStatus.Dispatched"/> is nobody's to re-plan,
    /// and a window that closes exactly as the horizon opens is not in it.
    /// </summary>
    [Fact]
    public async Task ListsOnlySchedulableJobsWhoseWindowMeetsTheHorizon()
    {
        var org = OrgId.New();
        var inside = Booked(org, Day.Start.AddHours(1), Day.Start.AddHours(3));
        var alsoInside = Booked(org, Day.Start.AddHours(2), Day.Start.AddHours(4));
        alsoInside.Schedule();
        alsoInside.Dispatch();
        var underWay = Booked(org, Day.Start.AddHours(1), Day.Start.AddHours(3));
        underWay.Schedule();
        underWay.Dispatch();
        underWay.MarkEnRoute();
        var cancelled = Booked(org, Day.Start.AddHours(1), Day.Start.AddHours(3));
        cancelled.Cancel();
        var touchingTheOpening = Booked(org, Day.Start.AddHours(-2), Day.Start);
        var afterTheClose = Booked(org, Day.End, Day.End.AddHours(2));

        await using (var write = _postgres.NewContext())
        {
            write.Jobs.AddRange(inside, alsoInside, underWay, cancelled, touchingTheOpening, afterTheClose);
            await write.SaveChangesAsync();
        }

        using var scope = _postgres.CreateScope();
        var schedulable = await scope.ServiceProvider
            .GetRequiredService<IJobRepository>()
            .ListSchedulableAsync(Day, CancellationToken.None);

        var mine = schedulable.Where(job => job.OrgId == org).Select(job => job.Id).ToList();

        Assert.Equal([inside.Id, alsoInside.Id], mine);
    }

    [Fact]
    public async Task ListsThePlanInTheHorizonInTheOrderItIsDriven()
    {
        var org = OrgId.New();
        var second = Planned(org, Day.Start.AddHours(4));
        var first = Planned(org, Day.Start.AddHours(1));
        var tomorrow = Planned(org, Day.End.AddHours(2));

        await using (var write = _postgres.NewContext())
        {
            write.Assignments.AddRange(second, first, tomorrow);
            await write.SaveChangesAsync();
        }

        using var scope = _postgres.CreateScope();
        var plan = await scope.ServiceProvider
            .GetRequiredService<IAssignmentRepository>()
            .ListInHorizonAsync(Day, CancellationToken.None);

        var mine = plan.Where(assignment => assignment.OrgId == org).Select(assignment => assignment.Id).ToList();

        Assert.Equal([first.Id, second.Id], mine);
    }

    /// <summary>
    /// A job is planned once, so this either finds the stop or says there is not one — and the
    /// second half is what the assign path branches on.
    /// </summary>
    [Fact]
    public async Task FindsTheStopPlannedForAJobAndNothingForAnUnplannedOne()
    {
        var planned = Planned(OrgId.New(), Day.Start.AddHours(2));

        await using (var write = _postgres.NewContext())
        {
            write.Assignments.Add(planned);
            await write.SaveChangesAsync();
        }

        using var scope = _postgres.CreateScope();
        var assignments = scope.ServiceProvider.GetRequiredService<IAssignmentRepository>();

        Assert.Equal(
            planned.Id,
            (await assignments.GetByJobAsync(planned.JobId, CancellationToken.None))?.Id);
        Assert.Null(await assignments.GetByJobAsync(JobId.New(), CancellationToken.None));
    }

    /// <summary>
    /// The one aggregate that is genuinely deleted. Re-optimising a day that can no longer fit a
    /// job must leave no stop behind for it.
    /// </summary>
    [Fact]
    public async Task RemovesAStopFromThePlan()
    {
        var planned = Planned(OrgId.New(), Day.Start.AddHours(3));

        await using (var write = _postgres.NewContext())
        {
            write.Assignments.Add(planned);
            await write.SaveChangesAsync();
        }

        using (var scope = _postgres.CreateScope())
        {
            var assignments = scope.ServiceProvider.GetRequiredService<IAssignmentRepository>();
            var stop = await assignments.GetAsync(planned.Id, CancellationToken.None);

            assignments.Remove(stop!);
            await scope.ServiceProvider
                .GetRequiredService<IUnitOfWork>()
                .SaveChangesAsync(CancellationToken.None);
        }

        using var read = _postgres.CreateScope();

        Assert.Null(await read.ServiceProvider
            .GetRequiredService<IAssignmentRepository>()
            .GetAsync(planned.Id, CancellationToken.None));
    }

    /// <summary>
    /// Half of a two-aggregate change landing is worse than neither half landing, which is the
    /// entire reason the repositories do not save. The second stop for an already-planned job
    /// breaks the unique index, and the job staged beside it must not survive that.
    /// </summary>
    [Fact]
    public async Task RollsBackEveryAggregateWhenOneOfThemFails()
    {
        var planned = Planned(OrgId.New(), Day.Start.AddHours(5));

        await using (var write = _postgres.NewContext())
        {
            write.Assignments.Add(planned);
            await write.SaveChangesAsync();
        }

        var job = JobBuilder.Any().Build();
        var duplicate = AssignmentBuilder.Any().ForJob(planned.JobId).Build();

        using (var scope = _postgres.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<IJobRepository>().Add(job);
            scope.ServiceProvider.GetRequiredService<IAssignmentRepository>().Add(duplicate);

            await Assert.ThrowsAsync<DbUpdateException>(() => scope.ServiceProvider
                .GetRequiredService<IUnitOfWork>()
                .SaveChangesAsync(CancellationToken.None));
        }

        using var read = _postgres.CreateScope();

        Assert.Null(await read.ServiceProvider
            .GetRequiredService<IJobRepository>()
            .GetAsync(job.Id, CancellationToken.None));
    }

    private static Job Booked(OrgId org, DateTimeOffset opens, DateTimeOffset closes) =>
        JobBuilder.Any().ForOrg(org).InWindow(new TimeWindow(opens, closes)).Build();

    private static Domain.Assignments.Assignment Planned(OrgId org, DateTimeOffset start) =>
        AssignmentBuilder.Any().ForOrg(org).ForJob(JobId.New()).StartingAt(start).Build();
}
