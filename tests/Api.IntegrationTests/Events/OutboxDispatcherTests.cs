using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs.AssignJob;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Infrastructure.Events;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Events;

/// <summary>
/// The background dispatcher — the thing that actually runs in a deployed host.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Everything here was previously unreachable from a test, and both defects lived in it.</strong>
/// <c>OutboxSweep</c> was extracted from the timer "so a test can drive a sweep, not wait for one",
/// and the suite duly drove <c>OutboxSweep</c> — from a scope with a tenant already resolved, which
/// a background service does not have. The half that ran in production was never executed by a test
/// at all, and it could not deliver a single message: every subscriber it publishes to reads
/// <c>ITenantContext.OrgId</c>, which throws when nobody has resolved one, so each message was
/// marked failed and left. The outbox filled up instead of catching anything.
/// </para>
/// <para>
/// So these two drive <c>OutboxDispatcher</c> and <c>OutboxTrail</c> directly, with no ambient
/// tenant, which is the arrangement a deployed host is in.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class OutboxDispatcherTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    private readonly OrgId _tenant = OrgId.New();
    private readonly BrittleBoardNotifier _board = new();
    private readonly PostgresFixture _postgres;

    public OutboxDispatcherTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>
    /// A reaction the request lost is delivered by the background sweep — with nobody having
    /// resolved a tenant for it, because in a running host nobody has.
    /// </summary>
    /// <remarks>
    /// This is the test that fails without the fix. The dispatcher opened one bare scope and swept;
    /// the first subscriber read the tenant, threw, and the message was recorded as failed forever.
    /// Note what is deliberately absent below: any call to <c>ActingAs</c>.
    /// </remarks>
    [Fact]
    public async Task TheBackgroundSweepDeliversWithNoRequestToBorrowATenantFrom()
    {
        await using var services = BuildHost();
        var job = await APlannedJobAsync(services);

        // The board push throws, so the cancellation commits and its reaction is lost — the case
        // the outbox exists for.
        _board.ThrowOnce = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Send(services, new ChangeJobStatusCommand(job, JobStatus.Cancelled)));

        await using (var pending = _postgres.NewContext(_tenant))
        {
            // Scoped to this test's tenant: the outbox has no query filter — the sweep must be able
            // to find work before anybody is resolved — so an unscoped read sees every class that
            // shares the container.
            Assert.NotEmpty(await Mine(pending).ToListAsync());
        }

        // Exactly what the hosted service does every ten seconds, and nothing more. No tenant.
        var dispatcher = new OutboxDispatcher(
            services.GetRequiredService<IServiceScopeFactory>(),
            new OutboxOptions(GracePeriod: TimeSpan.Zero),
            NullLogger<OutboxDispatcher>.Instance);

        await dispatcher.SweepAsync(CancellationToken.None);

        await using var reading = _postgres.NewContext(_tenant);

        // Delivered a second time, the reaction's work has landed, and the row is gone rather than
        // sitting there with "No tenant has been resolved for this request" on it.
        Assert.Equal(2, _board.Pushed.Count(status => status == JobStatus.Cancelled));
        Assert.Empty(await reading.Assignments.Where(stop => stop.JobId == job).ToListAsync());
        Assert.NotEmpty(await reading.SyncRemovals.ToListAsync());
        Assert.Empty(await Mine(reading).ToListAsync());
    }

    /// <summary>
    /// One organization's successful delivery does not delete another's undelivered work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second defect, and the sharper of the two. <c>OutboxTrail.ForgetAsync</c> matched rows on
    /// the event's type and <c>OccurredAt &gt;= oldest</c> — against the one table in the schema with
    /// no tenant query filter. So a tenant whose publish succeeded deleted every row of that type at
    /// or after that instant across the whole deployment, including rows belonging to a tenant whose
    /// own publish had just failed and which the sweep was going to redeliver.
    /// </para>
    /// <para>
    /// The two rows below share an instant exactly, which is the case the original prose said it was
    /// handling ("two events of one type at one instant are the same fact twice"). That is true
    /// within one tenant and false across two.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task OneTenantsDeliveryLeavesAnothersUndeliveredWorkAlone()
    {
        await using var services = BuildHost();

        var elsewhere = OrgId.New();
        var cancelled = new JobCancelled(JobId.New());

        // Two rows, one per organization, for the same event at the same instant.
        await using (var writing = _postgres.NewContext(_tenant))
        {
            writing.Outbox.Add(OutboxMessage.For(cancelled, "{}", _tenant));
            writing.Outbox.Add(OutboxMessage.For(cancelled, "{}", elsewhere));

            await writing.SaveChangesAsync();
        }

        // This tenant delivers its own and forgets the row, exactly as a committed request does.
        await using (var mine = _postgres.NewContext(_tenant))
        {
            await OutboxTrail.ForgetAsync(mine, [cancelled], CancellationToken.None);
        }

        await using var reading = _postgres.NewContext(elsewhere);

        // Theirs survives…
        var theirs = await reading.Outbox.Where(message => message.OrgId == elsewhere).ToListAsync();
        Assert.Single(theirs);

        // …and mine is the one that went, which is what says the delete happened at all rather than
        // this passing because nothing was deleted.
        Assert.Empty(await Mine(reading).ToListAsync());
    }

    /// <summary>This test's own outbox rows, in a table the whole suite shares.</summary>
    private IQueryable<OutboxMessage> Mine(Infrastructure.Persistence.AppDbContext context) =>
        context.Outbox.Where(message => message.OrgId == _tenant);

    /// <summary>A job on a technician's day, so cancelling it has a stop to withdraw.</summary>
    private async Task<JobId> APlannedJobAsync(ServiceProvider services)
    {
        var customer = await Send(services, new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value, "Head office", "12 Bath Road, Slough", 51.5107d, -0.5950d));

        var technician = await Send(services, new CreateTechnicianCommand(
            "Sam Rivera", ["hvac"], MondayMorning, MondayMorning.AddHours(9), 51.5074d, -0.1278d));

        var job = await Send(services, new CreateJobCommand(
            customer.Value, location.Value, "hvac", JobPriority.Normal,
            MondayMorning, MondayMorning.AddHours(4), TimeSpan.FromHours(1)));

        await Send(services, new AssignJobCommand(job.Value, technician.Value, MondayMorning.AddHours(1)));

        return job.Value;
    }

    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres)
            .AddSingleton<IBoardNotifier>(_board)
            .BuildServiceProvider(validateScopes: true);

    private async Task<TResponse> Send<TResponse>(ServiceProvider services, IRequest<TResponse> request)
    {
        using var scope = services.ActingAs(_tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}
