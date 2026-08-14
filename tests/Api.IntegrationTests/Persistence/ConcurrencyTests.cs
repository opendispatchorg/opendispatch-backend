using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Assignments;
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

    /// <summary>
    /// The same race, caught by the other lock: two dispatchers planning one job at the same
    /// moment, or two re-plans of one day running side by side.
    /// </summary>
    /// <remarks>
    /// Neither writer read a stop, so there is no <c>Version</c> to be stale — the one-stop-per-job
    /// index is what refuses the second, and Postgres reports it as a unique violation rather than
    /// as a concurrency conflict. Untranslated it reached the caller as a 500, which is the same
    /// misreport <c>ConcurrencyConflictException</c> exists to prevent one lock over.
    /// </remarks>
    [Fact]
    public async Task TheSecondPlanForOneJobIsRefusedRatherThanCrashing()
    {
        await using var services = TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);
        var job = JobId.New();
        var technician = TechnicianId.New();
        var when = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

        using (var first = services.ActingAs(_tenant))
        {
            first.ServiceProvider.GetRequiredService<IAssignmentRepository>()
                .Add(Assignment.Create(_tenant, job, technician, 0, when, 10d));

            await first.ServiceProvider.GetRequiredService<IUnitOfWork>()
                .SaveChangesAsync(CancellationToken.None);
        }

        using var second = services.ActingAs(_tenant);
        second.ServiceProvider.GetRequiredService<IAssignmentRepository>()
            .Add(Assignment.Create(_tenant, job, technician, 1, when, 10d));

        await Assert.ThrowsAsync<DuplicateRecordException>(
            () => second.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None));

        // One stop, the winner's, and it is the whole of the point: the loser's write is refused
        // rather than half-applied.
        await using var context = _postgres.NewContext(_tenant);
        Assert.Equal(0, (await context.Assignments.SingleAsync(stop => stop.JobId == job)).Sequence);
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
