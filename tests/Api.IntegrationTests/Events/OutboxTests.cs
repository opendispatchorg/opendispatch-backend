using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs.AssignJob;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Infrastructure.Events;
using OpenDispatch.Infrastructure.Persistence;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Events;

/// <summary>
/// What happens to a reaction the request that raised it failed to deliver.
/// </summary>
/// <remarks>
/// <para>
/// Until the outbox, the answer was "nothing, silently". Events lived in memory between the save
/// and the commit and were published in-process afterwards; a subscriber that threw, or a host that
/// died in the moment after committing, lost the reaction for good. The shape that matters in this
/// system is a cancelled job whose stop stays on a technician's phone — committed cancellation, no
/// withdrawal, nobody told.
/// </para>
/// <para>
/// Every fact here needs the real database: the guarantee is that the row and the work are one
/// write, which is a claim about a transaction rather than about C#.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class OutboxTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    /// <summary>An event type no build has: what a row written before a rename looks like.</summary>
    private const string RenamedEvent = "OpenDispatch.Domain.Events.SomethingRenamed";

    private readonly OrgId _tenant = OrgId.New();
    private readonly BrittleBoardNotifier _board = new();
    private readonly PostgresFixture _postgres;

    public OutboxTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>
    /// The whole point: a reaction that failed in the request is still delivered afterwards, and the
    /// work it does lands.
    /// </summary>
    [Fact]
    public async Task AReactionLostByTheRequestIsDeliveredByTheSweep()
    {
        await using var services = BuildHost();
        var job = await APlannedJobAsync(services);

        // The board push throws, which fails the request — the cancellation is already committed by
        // then, which is exactly the case that used to lose the reaction.
        _board.ThrowOnce = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Send(services, new ChangeJobStatusCommand(job, JobStatus.Cancelled)));

        await using (var context = _postgres.NewContext(_tenant))
        {
            // Committed: the job is called off, whatever became of the reaction…
            Assert.Equal(
                JobStatus.Cancelled,
                (await context.Jobs.SingleAsync(saved => saved.Id == job)).Status);

            // …and the fact is written down, waiting, because the publish that would have deleted
            // the row threw before it got there.
            Assert.NotEmpty(await MineAsync(context, job));
        }

        // The board was told once about the cancellation, and fell over.
        Assert.Equal(1, _board.Pushed.Count(status => status == JobStatus.Cancelled));

        // The sweep, with no grace period, is what a running host does thirty seconds later.
        using (var scope = services.ActingAs(_tenant))
        {
            var delivered = await scope.ServiceProvider.GetRequiredService<OutboxSweep>()
                .DeliverPendingAsync(TimeSpan.Zero, batchSize: 50, CancellationToken.None);

            Assert.True(delivered > 0);
        }

        await using var reading = _postgres.NewContext(_tenant);

        // Delivered again — which is the guarantee — and the reaction's work has landed: the stop is
        // withdrawn and the technician will be told by their next pull.
        Assert.Equal(2, _board.Pushed.Count(status => status == JobStatus.Cancelled));
        Assert.Empty(await reading.Assignments.Where(stop => stop.JobId == job).ToListAsync());
        Assert.NotEmpty(await reading.SyncRemovals.ToListAsync());
        Assert.Empty(await MineAsync(reading, job));
    }

    /// <summary>
    /// The ordinary path leaves nothing behind, or every sweep would deliver everything twice.
    /// </summary>
    [Fact]
    public async Task ASuccessfulRequestClearsItsOwnMessages()
    {
        await using var services = BuildHost();
        var job = await APlannedJobAsync(services);

        var cancelled = await Send(services, new ChangeJobStatusCommand(job, JobStatus.Cancelled));
        Assert.True(cancelled.IsSuccess);

        await using var context = _postgres.NewContext(_tenant);

        Assert.Empty(await MineAsync(context, job));
        Assert.Empty(await context.Assignments.Where(stop => stop.JobId == job).ToListAsync());
    }

    /// <summary>
    /// A message nobody can deliver stays, with what went wrong on it.
    /// </summary>
    /// <remarks>
    /// The failure mode a retry loop hides: a payload this build cannot read — written by a version
    /// that has since renamed its event — must not vanish and must not be retried forever in
    /// silence. It is counted, recorded and left, so the row is the evidence and the size of this
    /// table is the alert.
    /// </remarks>
    [Fact]
    public async Task AMessageThatCannotBeDeliveredIsKeptWithItsError()
    {
        await using var services = BuildHost();

        await using (var writing = _postgres.NewContext(_tenant))
        {
            var payload = "{}";

            await writing.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO outbox_messages (id, org_id, type, payload, occurred_at, attempts, change_seq)
                VALUES ({Guid.NewGuid()}, {_tenant.Value}, {RenamedEvent},
                        CAST({payload} AS jsonb), {MondayMorning}, 0, 0)
                """);
        }

        using (var scope = services.ActingAs(_tenant))
        {
            // What the sweep delivered in total is not this test's to claim: the shared container
            // carries whatever earlier classes left in it. What is this test's is the row below —
            // which now needs an org_id, because a sweep claims one tenant's messages at a time.
            await scope.ServiceProvider.GetRequiredService<OutboxSweep>()
                .DeliverPendingAsync(TimeSpan.Zero, batchSize: 50, CancellationToken.None);
        }

        await using var reading = _postgres.NewContext(_tenant);
        var stuck = Assert.Single(await reading.Outbox
            .Where(message => message.Type == RenamedEvent)
            .ToListAsync());

        Assert.Equal(1, stuck.Attempts);
        Assert.NotNull(stuck.LastError);
        Assert.Contains("SomethingRenamed", stuck.LastError, StringComparison.Ordinal);
    }

    /// <summary>
    /// This test's own outbox rows.
    /// </summary>
    /// <remarks>
    /// The outbox carries no <c>OrgId</c> — it belongs to the delivery mechanism rather than to a
    /// tenant — so it is the one table in this suite that a test cannot assert about wholesale: the
    /// shared container is running other classes at the same time, and their messages are in it too.
    /// The job's id is in the payload, which is what makes a message findable as this test's.
    /// </remarks>
    private static async Task<List<string>> MineAsync(AppDbContext context, JobId job)
    {
        // `LIKE` over a jsonb column has no operator in Postgres, so the cast is explicit and the
        // match is done in SQL rather than by pulling every message back to compare here.
        var needle = $"%{job.Value}%";

        return await context.Outbox
            .FromSql($"SELECT * FROM outbox_messages WHERE payload::text LIKE {needle}")
            .Select(message => message.Type)
            .ToListAsync();
    }

    /// <remarks>
    /// The board notifier is replaced rather than a subscriber added: a handler in this assembly is
    /// found by every other class's scan too, and would attach itself to their hosts — the trap
    /// <c>ReactionTransactionTests</c> already names. Failing inside <c>BoardNotifications</c>, a
    /// real subscriber to <c>JobCancelled</c>, is also the more faithful failure.
    /// </remarks>
    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres)
            .AddSingleton<IBoardNotifier>(_board)
            .BuildServiceProvider(validateScopes: true);

    private async Task<JobId> APlannedJobAsync(ServiceProvider services)
    {
        var customer = await Send(services, new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value, "Head office", "12 Bath Road, Slough", 51.5107, -0.5950));

        var job = await Send(services, new CreateJobCommand(
            customer.Value,
            location.Value,
            "hvac",
            JobPriority.Normal,
            MondayMorning,
            MondayMorning.AddHours(3),
            TimeSpan.FromHours(1)));

        var technician = await Send(services, new CreateTechnicianCommand(
            "Sam Rivera", ["hvac"], MondayMorning.AddHours(-1), MondayMorning.AddHours(8), 51.5074, -0.1278));

        var assigned = await Send(services, new AssignJobCommand(job.Value, technician.Value, MondayMorning));
        Assert.True(assigned.IsSuccess);

        return job.Value;
    }

    private async Task<TResponse> Send<TResponse>(ServiceProvider services, IRequest<TResponse> request)
    {
        using var scope = services.ActingAs(_tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}

/// <summary>
/// A board notifier that can be made to fail once, so a test can lose a reaction the way a real one
/// is lost.
/// </summary>
/// <remarks>
/// <c>BoardNotifications</c> — a real subscriber to <c>JobCancelled</c> — pushes through this, so a
/// throw here is a delivery that failed after the work had committed: the exact shape the outbox
/// exists for.
/// </remarks>
internal sealed class BrittleBoardNotifier : IBoardNotifier
{
    /// <summary>Whether the next push throws.</summary>
    public bool ThrowOnce { get; set; }

    /// <summary>
    /// Every status pushed, including the push that threw — and including the ones the arrange step
    /// produces, which is why the assertions count a status rather than a total.
    /// </summary>
    public List<JobStatus> Pushed { get; } = [];

    public Task JobUpdatedAsync(JobId jobId, JobStatus status, long version, CancellationToken ct)
    {
        Pushed.Add(status);

        if (ThrowOnce)
        {
            ThrowOnce = false;

            throw new InvalidOperationException("the board push fell over");
        }

        return Task.CompletedTask;
    }

    public Task AssignmentUpdatedAsync(
        AssignmentId assignmentId,
        JobId jobId,
        TechnicianId technicianId,
        int sequence,
        DateTimeOffset scheduledStart,
        double travelMin,
        long version,
        CancellationToken ct) => Task.CompletedTask;
}
