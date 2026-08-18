using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Sync;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Infrastructure.Provisioning;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Sync;

/// <summary>
/// What the <c>prune</c> verb deletes, and what it must not.
/// </summary>
/// <remarks>
/// The op log and the removal notes are the two tables in this system that nothing else ever
/// deletes a row from, which is why they needed a retention story at all. The half worth testing is
/// the boundary: a shop that prunes must lose the history it asked to lose and keep everything else,
/// including the business records that share the database with it.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class SyncLogRetentionTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public SyncLogRetentionTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task DeletesWhatIsPastTheWindowAndKeepsWhatIsInsideIt()
    {
        var technician = TechnicianId.New();

        var old = SyncOpId.From(Guid.NewGuid());
        var recent = SyncOpId.From(Guid.NewGuid());
        var oldStop = AssignmentId.New();
        var recentStop = AssignmentId.New();

        await using (var context = _postgres.NewContext(_tenant))
        {
            context.SyncOps.Add(Applied(old, technician, Now.AddDays(-40)));
            context.SyncOps.Add(Applied(recent, technician, Now.AddDays(-3)));
            context.SyncRemovals.Add(SyncRemoval.OfStop(oldStop, _tenant, technician, Now.AddDays(-40)));
            context.SyncRemovals.Add(SyncRemoval.OfStop(recentStop, _tenant, technician, Now.AddDays(-3)));

            await context.SaveChangesAsync();
        }

        await using var services = TestHost.Over(_postgres)
            .AddSingleton<IClock>(new FixedNow())
            .BuildServiceProvider(validateScopes: true);

        using (var scope = services.CreateScope())
        {
            var pruned = await scope.ServiceProvider.GetRequiredService<SyncLogPruner>()
                .PruneAsync(TimeSpan.FromDays(30), CancellationToken.None);

            Assert.Equal(1, pruned.Operations);
            Assert.Equal(1, pruned.Removals);
        }

        await using var reading = _postgres.NewContext(_tenant);

        Assert.Equal([recent], await reading.SyncOps.Select(op => op.Id).ToListAsync());
        Assert.Equal(
            [recentStop.Value],
            await reading.SyncRemovals.Select(removal => removal.EntityId).ToListAsync());
    }

    /// <summary>
    /// Nothing but the two bookkeeping tables. A prune that took a job with it would be deleting the
    /// shop's records, which is the opposite of what this system promises.
    /// </summary>
    [Fact]
    public async Task LeavesTheBusinessRecordsAlone()
    {
        await using var services = TestHost.Over(_postgres)
            .AddSingleton<IClock>(new FixedNow())
            .BuildServiceProvider(validateScopes: true);

        var job = JobBuilder.Any().ForOrg(_tenant).Build();

        await using (var context = _postgres.NewContext(_tenant))
        {
            context.Jobs.Add(job);
            await context.SaveChangesAsync();
        }

        using (var scope = services.CreateScope())
        {
            // Everything, however old: a window of nothing is the most aggressive prune available.
            await scope.ServiceProvider.GetRequiredService<SyncLogPruner>()
                .PruneAsync(TimeSpan.Zero, CancellationToken.None);
        }

        await using var reading = _postgres.NewContext(_tenant);
        Assert.Contains(job.Id, await reading.Jobs.Select(saved => saved.Id).ToListAsync());
    }

    private SyncOpRecord Applied(SyncOpId id, TechnicianId technician, DateTimeOffset at) =>
        SyncOpRecord.Applied(
            id,
            _tenant,
            technician,
            FieldOps.JobEntity,
            Guid.NewGuid(),
            FieldOps.AddNote,
            """{"text":"Meter behind the boiler."}""",
            0,
            at,
            at);

    /// <summary>A clock the prune window is measured from, so "old" does not depend on the day.</summary>
    private sealed class FixedNow : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
