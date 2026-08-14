using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;
using OpenDispatch.Application.Scheduling.OptimizeDay;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Infrastructure.Seeding;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Seeding;

/// <summary>
/// The demo dataset against a real database.
/// </summary>
/// <remarks>
/// <para>
/// The claim worth testing is the one Document 3 makes: seeding produces a <em>schedulable</em>
/// dataset. That is not a property of the seeder's code — it is a property of the numbers in
/// <c>DemoData</c>, and it is genuinely easy to break by tightening a shift or lengthening a job.
/// So the test is the real thing end to end: seed, hand the day to the real optimiser through the
/// real pipeline, and count what got planned.
/// </para>
/// <para>
/// The rest is what makes the demo usable rather than merely present: a second run leaves one
/// day's work rather than two, and the field login resolves to a technician who exists.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class DemoSeedFlowTests
{
    /// <summary>
    /// How much of the day the optimiser has to place for the demo to be worth showing.
    /// </summary>
    /// <remarks>
    /// Not all of it, deliberately. The dataset is sized at roughly ninety per cent utilisation
    /// with scarce skills, so a handful of jobs genuinely may not fit — that is the unassigned
    /// pile doing its job. A threshold of "everything" would be a demand that the data be easy.
    /// </remarks>
    private const double MostOfTheDay = 0.85d;

    private readonly PostgresFixture _postgres;

    public DemoSeedFlowTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task SeedsADayTheOptimiserCanPlanMostOf()
    {
        await using var services = BuildHost();
        var seed = await SeedAsync(services);

        Assert.Equal(8, seed.Technicians);
        Assert.Equal(40, seed.Jobs);

        using var planning = services.ActingAs(seed.Organization);
        var optimized = await planning.ServiceProvider.GetRequiredService<ISender>()
            .Send(new OptimizeDayCommand(seed.Day.Start, seed.Day.End));

        Assert.True(optimized.IsSuccess, optimized.Error?.Message);
        Assert.True(
            optimized.Value.Planned >= seed.Jobs * MostOfTheDay,
            $"The optimiser placed {optimized.Value.Planned} of {seed.Jobs} seeded jobs, "
                + $"which is under the {MostOfTheDay:P0} a demo needs. "
                + "Either the crew's shifts or the jobs' durations have drifted.");

        // Planned work is planned work: every stop the optimiser wrote left its job Scheduled
        // rather than the Unscheduled the seeder booked it as.
        await using var context = _postgres.NewContext(seed.Organization);
        Assert.Equal(
            optimized.Value.Planned,
            await context.Jobs.CountAsync(job => job.Status == JobStatus.Scheduled));
    }

    [Fact]
    public async Task SeedingTwiceLeavesOneDaysWorkAndTheSameTenant()
    {
        await using var services = BuildHost();

        var first = await SeedAsync(services);
        var second = await SeedAsync(services);

        // The organization survives, so a token issued against the first run still names a tenant
        // that exists after the second.
        Assert.Equal(first.Organization, second.Organization);

        await using var context = _postgres.NewContext(second.Organization);
        Assert.Equal(second.Jobs, await context.Jobs.CountAsync());
        Assert.Equal(second.Technicians, await context.Technicians.CountAsync());
        Assert.Equal(second.Customers, await context.Customers.CountAsync());
    }

    [Fact]
    public async Task TheFieldLoginIsATechnicianWhoExists()
    {
        await using var services = BuildHost();
        var seed = await SeedAsync(services);

        using (var scope = services.CreateScope())
        {
            Assert.Equal(
                seed.Logins.Count,
                await scope.ServiceProvider.GetRequiredService<DemoSeeder>()
                    .RegisterLoginsAsync(CancellationToken.None));
        }

        // In a scope of its own: the store reads the users table through the request's context now
        // that a login is a row rather than a dictionary entry.
        using var reading = services.CreateScope();
        var users = reading.ServiceProvider.GetRequiredService<IUserStore>();
        var expected = seed.Logins.Single(login => login.Role == UserRole.Technician);
        var registered = await users.FindByUsernameAsync(expected.Username, CancellationToken.None);

        Assert.NotNull(registered);
        Assert.Equal(seed.Organization, registered.OrgId);
        Assert.NotNull(registered.TechnicianId);

        // The point of the whole login: /sync scopes a request to the technician behind the token,
        // so this has to be a technician the seeder actually wrote.
        await using var context = _postgres.NewContext(seed.Organization);
        var technician = registered.TechnicianId.Value;
        Assert.Equal(
            expected.TechnicianName,
            await context.Technicians.Where(candidate => candidate.Id == technician)
                .Select(candidate => candidate.Name)
                .SingleAsync());

        // The office logins are nobody's field identity, which is what keeps a dispatcher's token
        // out of a technician's sync scope.
        var admin = await users.FindByUsernameAsync("admin", CancellationToken.None);
        Assert.Null(admin?.TechnicianId);
    }

    [Fact]
    public void TheSeederDoesNotExistOutsideDevelopment()
    {
        using var production = TestHost.Over(_postgres)
            .AddDemoSeeding("Production")
            .BuildServiceProvider(validateScopes: true);

        using var scope = production.CreateScope();

        // Not "refuses when asked" — not there to ask. That is the guard: a host that is not a
        // development one has no seeder in its container to reach, however it is reached for.
        Assert.Null(scope.ServiceProvider.GetService<DemoSeeder>());
    }

    private static async Task<DemoSeed> SeedAsync(ServiceProvider services)
    {
        // A scope of its own with no tenant resolved in it, which is what the seeder gets from
        // `make seed` and from the host's startup: every read it makes has to say which
        // organization it means rather than rely on an ambient one.
        using var scope = services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<DemoSeeder>()
            .SeedAsync(CancellationToken.None);
    }

    private ServiceProvider BuildHost() => TestHost.Over(_postgres)
        .AddDemoSeeding(DemoSeeding.RequiredEnvironment)
        .BuildServiceProvider(validateScopes: true);
}
