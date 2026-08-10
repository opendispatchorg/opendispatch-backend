using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Organizations;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Infrastructure.Persistence;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Persistence;

/// <summary>
/// Two organizations, live in the same process, sharing one model and one connection pool.
/// </summary>
/// <remarks>
/// <para>
/// This is the test the whole step exists for, and the arrangement is the point: both tenants run
/// through the same cached EF model, because a filter that baked one organization's id into that
/// model at first use would pass any test that only ever ran one tenant, and leak in production
/// from the second request onwards.
/// </para>
/// <para>
/// Every aggregate is checked rather than a representative one. A filter that reaches four out of
/// five is not four-fifths of a tenancy story; it is a leak, in whichever one was missed.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class TenantIsolationTests
{
    private readonly OrgId _acme = OrgId.New();
    private readonly OrgId _rival = OrgId.New();
    private readonly PostgresFixture _postgres;

    public TenantIsolationTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task OneTenantNeverSeesAnothersRows()
    {
        var mine = await SeedAsync(_acme);
        var theirs = await SeedAsync(_rival);

        await using var context = _postgres.NewContext(_acme);

        Assert.Equal([mine.Job], await context.Jobs.Select(job => job.Id).ToListAsync());
        Assert.Equal([mine.Assignment], await context.Assignments.Select(a => a.Id).ToListAsync());
        Assert.Equal([mine.Technician], await context.Technicians.Select(t => t.Id).ToListAsync());
        Assert.Equal([mine.Customer], await context.Customers.Select(c => c.Id).ToListAsync());
        Assert.Equal([mine.Invoice], await context.Invoices.Select(i => i.Id).ToListAsync());

        // The organization row is scoped too, by its own id: a tenant is one of the rows it is
        // not allowed to be shown a list of.
        Assert.Equal([_acme], await context.Organizations.Select(o => o.Id).ToListAsync());

        Assert.DoesNotContain(theirs.Job, await context.Jobs.Select(job => job.Id).ToListAsync());
    }

    /// <summary>
    /// Fetching by primary key is the path most likely to be assumed safe, and it is the one an
    /// id guessed or leaked from elsewhere would travel down.
    /// </summary>
    [Fact]
    public async Task AnotherTenantsRowIsNotFoundEvenByItsOwnId()
    {
        var theirs = await SeedAsync(_rival);

        using var scope = _postgres.ActingAs(_acme);

        Assert.Null(await scope.ServiceProvider.GetRequiredService<IJobRepository>()
            .GetAsync(theirs.Job, CancellationToken.None));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ICustomerRepository>()
            .GetAsync(theirs.Customer, CancellationToken.None));
        Assert.Null(await scope.ServiceProvider.GetRequiredService<IAssignmentRepository>()
            .GetAsync(theirs.Assignment, CancellationToken.None));
    }

    /// <summary>
    /// The repositories take no <c>OrgId</c>, so their queries are scoped by the filter or not at
    /// all — including the two the scheduler reads a whole horizon through.
    /// </summary>
    [Fact]
    public async Task TheSchedulerOnlyEverSeesItsOwnDay()
    {
        await SeedAsync(_acme);
        await SeedAsync(_rival);

        using var scope = _postgres.ActingAs(_acme);
        var horizon = new TimeWindow(
            DateTimeOffset.UtcNow.AddYears(-50),
            DateTimeOffset.UtcNow.AddYears(50));

        var jobs = await scope.ServiceProvider.GetRequiredService<IJobRepository>()
            .ListSchedulableAsync(horizon, CancellationToken.None);
        var plan = await scope.ServiceProvider.GetRequiredService<IAssignmentRepository>()
            .ListInHorizonAsync(horizon, CancellationToken.None);
        var crew = await scope.ServiceProvider.GetRequiredService<ITechnicianRepository>()
            .ListAsync(CancellationToken.None);

        Assert.All(jobs, job => Assert.Equal(_acme, job.OrgId));
        Assert.All(plan, assignment => Assert.Equal(_acme, assignment.OrgId));
        Assert.All(crew, technician => Assert.Equal(_acme, technician.OrgId));
        Assert.NotEmpty(jobs);
        Assert.NotEmpty(plan);
        Assert.NotEmpty(crew);
    }

    /// <summary>
    /// Nothing in the system may read tenant-scoped data without saying whose it is, and the
    /// failure has to be loud: a tenant context that returned a default organization would match
    /// nothing and present as a dispatch board that is simply empty.
    /// </summary>
    /// <remarks>
    /// Built from <c>AddPersistence</c> alone, without the fixture's test double over the top, so
    /// what is under test is the tenant context the host actually registers. Asserting this
    /// against the double would only prove the double throws.
    /// </remarks>
    [Fact]
    public async Task RefusesToQueryWhenNoTenantHasBeenResolved()
    {
        await SeedAsync(_acme);

        await using var services = new ServiceCollection()
            .AddPersistence(_ => _postgres.ConnectionString)
            .BuildServiceProvider();
        using var scope = services.CreateScope();
        var unresolved = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => unresolved.Jobs.ToListAsync());
    }

    private async Task<Seeded> SeedAsync(OrgId tenant)
    {
        var organization = Organization.Create("Riverside Heating");
        var customer = CustomerBuilder.Any().ForOrg(tenant).Build();
        var job = JobBuilder.Any().ForOrg(tenant).ForCustomer(customer.Id).Build();
        var technician = TechnicianBuilder.Any().ForOrg(tenant).Build();
        var assignment = AssignmentBuilder.Any()
            .ForOrg(tenant)
            .ForJob(job.Id)
            .ForTechnician(technician.Id)
            .Build();
        var invoice = InvoiceBuilder.Any().ForOrg(tenant).ForJob(job.Id).Build();

        await using var context = _postgres.NewContext(tenant);

        // Organization mints its own id, and the tenant these rows belong to has to be that id
        // for the filter to find it — so the row is written with the id under test rather than
        // the one Create invented.
        await context.Database.ExecuteSqlAsync(
            $"""INSERT INTO organizations (id, name, version) VALUES ({tenant.Value}, {organization.Name}, 0);""");

        context.Customers.Add(customer);
        context.Jobs.Add(job);
        context.Technicians.Add(technician);
        context.Assignments.Add(assignment);
        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();

        return new Seeded(job.Id, assignment.Id, technician.Id, customer.Id, invoice.Id);
    }

    private sealed record Seeded(
        JobId Job,
        AssignmentId Assignment,
        TechnicianId Technician,
        CustomerId Customer,
        InvoiceId Invoice);
}
