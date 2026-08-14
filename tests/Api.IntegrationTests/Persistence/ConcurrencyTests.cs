using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Persistence;

/// <summary>
/// Two dispatchers, one job, against a real database.
/// </summary>
/// <remarks>
/// <para>
/// The <c>Version</c> token has been on every aggregate root since step 25 and nothing had ever
/// checked what happens when it does its job. The answer, until this pass, was an unhandled
/// <c>DbUpdateConcurrencyException</c>: an error-level log and a 500 for the most ordinary
/// collision a dispatch board has.
/// </para>
/// <para>
/// Only a real database can settle this. The conflict is Postgres reporting that an <c>UPDATE …
/// WHERE version = @stale</c> matched no rows, which no fake unit of work can produce — the
/// pipeline's half of it (a refusal rather than a throw, with the transaction rolled back) is
/// proved without one in <c>Application.Tests</c>.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class ConcurrencyTests
{
    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public ConcurrencyTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task TheSecondWriterOfTheSameJobIsRefusedRatherThanSilentlyWinning()
    {
        await using var services = TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);
        var job = await ABookedJobAsync();

        // Both open the job. Each request is its own scope, its own context, its own tracked copy
        // — which is exactly the arrangement two dispatchers looking at one board produce.
        using var slow = services.ActingAs(_tenant);
        var slowCopy = await slow.ServiceProvider.GetRequiredService<IJobRepository>()
            .GetAsync(job, CancellationToken.None);

        using (var quick = services.ActingAs(_tenant))
        {
            var quickCopy = await quick.ServiceProvider.GetRequiredService<IJobRepository>()
                .GetAsync(job, CancellationToken.None);

            quickCopy!.Schedule();
            await quick.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .SaveChangesAsync(CancellationToken.None);
        }

        // The slow one acts on what it read, which was true when it read it.
        slowCopy!.Cancel();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => slow.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));

        // And the refusal is a refusal, not a partial write: the job is what the winner made it.
        await using var context = _postgres.NewContext(_tenant);
        var stored = await context.Jobs.SingleAsync(candidate => candidate.Id == job);

        Assert.Equal(JobStatus.Scheduled, stored.Status);
        Assert.Equal(1, stored.Version);
    }

    private async Task<JobId> ABookedJobAsync()
    {
        var job = JobBuilder.Any().ForOrg(_tenant).WithSkill("hvac").Build();

        await using var context = _postgres.NewContext(_tenant);
        context.Jobs.Add(job);
        await context.SaveChangesAsync();

        return job.Id;
    }
}
