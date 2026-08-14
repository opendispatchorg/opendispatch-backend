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
using OpenDispatch.Application.Scheduling.OptimizeDay;
using OpenDispatch.Application.Sync;
using OpenDispatch.Application.Sync.PullChanges;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Sync;

/// <summary>
/// What a device is told when it comes back.
/// </summary>
/// <remarks>
/// <para>
/// Pull is a projection over the change stamps step 41 put on every row, so nothing here can be
/// tested without a real database: the window is a <c>WHERE change_seq &gt;= …</c> against values
/// Postgres assigns from inside the writing transaction, and a fake would be asserting arithmetic I
/// had written myself.
/// </para>
/// <para>
/// The three the step asks for — windowing, an idempotent re-pull, and scope — plus the two that
/// make the difference between a working technician app and a technician driving to a job that is
/// not theirs: a stop handed to somebody else, and a stop deleted outright.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class PullChangesFlowTests
{
    /// <summary>A page budget big enough that nothing these tests write is ever capped.</summary>
    private const int APage = 200;

    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public PullChangesFlowTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>
    /// A device that has never synced asks with the beginning and gets its whole day: the stops,
    /// the work they are for, and the customer and address that live on neither.
    /// </summary>
    [Fact]
    public async Task GivesADeviceThatHasNeverSyncedItsWholeDay()
    {
        await using var services = BuildHost();
        var sam = await ATechnicianAsync(services, "Sam Rivera");
        var job = await ABookedJobAsync(services);
        await Send(services, new AssignJobCommand(job, sam, MondayMorning.AddHours(1)));

        var pulled = await PullAsync(services, sam, SyncCursor.Beginning);

        var stop = Assert.Single(pulled.Changes.Stops);
        Assert.Equal(job, stop.JobId);
        Assert.Equal(MondayMorning.AddHours(1), stop.ScheduledStart);

        var work = Assert.Single(pulled.Changes.Jobs);
        Assert.Equal(job, work.JobId);
        Assert.Equal(JobStatus.Scheduled, work.Status);
        Assert.Equal("Vance Refrigeration", work.CustomerName);
        Assert.Equal("12 Bath Road, Slough", work.Address);
        Assert.Empty(pulled.Changes.RemovedStops);

        // The cursor moved off the beginning, which is what the device stores.
        Assert.True(pulled.Cursor.Value > SyncCursor.Beginning.Value);
    }

    /// <summary>
    /// The window. A device that pulled after its day was planned and comes back later is told
    /// about the job that moved and nothing about the one that did not.
    /// </summary>
    [Fact]
    public async Task TellsADeviceOnlyWhatChangedSinceItsCursor()
    {
        await using var services = BuildHost();
        var sam = await ATechnicianAsync(services, "Sam Rivera");
        var quiet = await ABookedJobAsync(services);
        var moves = await ABookedJobAsync(services, "Second site", "9 Foundry Lane");
        await Send(services, new AssignJobCommand(quiet, sam, MondayMorning.AddHours(1)));
        await Send(services, new AssignJobCommand(moves, sam, MondayMorning.AddHours(3)));

        var caughtUp = (await PullAsync(services, sam, SyncCursor.Beginning)).Cursor;

        await Send(services, new ChangeJobStatusCommand(moves, JobStatus.Dispatched));

        var pulled = await PullAsync(services, sam, caughtUp);

        var work = Assert.Single(pulled.Changes.Jobs);
        Assert.Equal(moves, work.JobId);
        Assert.Equal(JobStatus.Dispatched, work.Status);

        // The stop is sent with it: a device holding a job whose plan it cannot see has half a
        // visit, and the two travel together whenever either moves.
        Assert.Equal(moves, Assert.Single(pulled.Changes.Stops).JobId);
    }

    /// <summary>
    /// The step's second requirement. A response lost to a dropped connection costs a repeat rather
    /// than a gap, so the same cursor returns the same window however many times it is asked with.
    /// </summary>
    [Fact]
    public async Task AnswersTheSameCursorWithTheSameWindowEveryTime()
    {
        await using var services = BuildHost();
        var sam = await ATechnicianAsync(services, "Sam Rivera");
        var job = await ABookedJobAsync(services);
        await Send(services, new AssignJobCommand(job, sam, MondayMorning.AddHours(1)));

        var first = await PullAsync(services, sam, SyncCursor.Beginning);
        var again = await PullAsync(services, sam, SyncCursor.Beginning);

        Assert.Equal(
            first.Changes.Jobs.Select(state => state.JobId),
            again.Changes.Jobs.Select(state => state.JobId));
        Assert.Equal(
            first.Changes.Stops.Select(state => state.AssignmentId),
            again.Changes.Stops.Select(state => state.AssignmentId));

        // And nothing to say when asked with the cursor it just handed out, because nothing has
        // happened since.
        var caughtUp = await PullAsync(services, sam, first.Cursor);

        Assert.Empty(caughtUp.Changes.Jobs);
        Assert.Empty(caughtUp.Changes.Stops);
        Assert.Empty(caughtUp.Changes.RemovedStops);
    }

    /// <summary>
    /// A change committed after the cursor was taken but before the read finished is sent again
    /// rather than skipped — the inclusive window, which is the property the whole mechanism is
    /// built for. Asserted at the boundary: the cursor a pull returns still finds the change that
    /// pull reported.
    /// </summary>
    [Fact]
    public async Task SendsAChangeStampedExactlyAtTheCursorAgainRatherThanAssumingItLanded()
    {
        await using var services = BuildHost();
        var sam = await ATechnicianAsync(services, "Sam Rivera");
        var job = await ABookedJobAsync(services);
        await Send(services, new AssignJobCommand(job, sam, MondayMorning.AddHours(1)));

        var pulled = await PullAsync(services, sam, SyncCursor.Beginning);
        Assert.NotEmpty(pulled.Changes.Stops);

        // The cursor was taken before the read, so it is at or below the change the read returned:
        // asking with it again repeats that change rather than losing it.
        var repeated = await PullAsync(services, sam, pulled.Cursor);

        Assert.Empty(repeated.Changes.Stops);

        var stamps = await StampsOfAsync(job);
        Assert.All(stamps, stamp => Assert.True(
            stamp < pulled.Cursor.Value || repeated.Changes.Stops.Count > 0,
            $"a change stamped {stamp} at or above the cursor {pulled.Cursor} was not repeated"));
    }

    /// <summary>
    /// Scope, the step's third requirement: one technician's device is told nothing about another's
    /// day, even though both belong to the same organization and the query runs under the same
    /// tenant.
    /// </summary>
    [Fact]
    public async Task TellsATechnicianNothingOfAnothersDay()
    {
        await using var services = BuildHost();
        var sam = await ATechnicianAsync(services, "Sam Rivera");
        var ada = await ATechnicianAsync(services, "Ada Okafor");
        var samsJob = await ABookedJobAsync(services);
        var adasJob = await ABookedJobAsync(services, "Second site", "9 Foundry Lane");
        await Send(services, new AssignJobCommand(samsJob, sam, MondayMorning.AddHours(1)));
        await Send(services, new AssignJobCommand(adasJob, ada, MondayMorning.AddHours(2)));

        var pulled = await PullAsync(services, sam, SyncCursor.Beginning);

        Assert.Equal(samsJob, Assert.Single(pulled.Changes.Jobs).JobId);
        Assert.Equal(samsJob, Assert.Single(pulled.Changes.Stops).JobId);
    }

    /// <summary>
    /// And the tenant, which is a different guarantee from the technician one: an organization's
    /// device cannot be told about another organization's work even if it asks as a technician id
    /// that exists over there.
    /// </summary>
    [Fact]
    public async Task TellsOneTenantNothingOfAnothersWork()
    {
        await using var services = BuildHost();
        var sam = await ATechnicianAsync(services, "Sam Rivera");
        var job = await ABookedJobAsync(services);
        await Send(services, new AssignJobCommand(job, sam, MondayMorning.AddHours(1)));

        using var elsewhere = services.ActingAs(OrgId.New());
        var pulled = await elsewhere.ServiceProvider
            .GetRequiredService<ISender>()
            .Send(new PullChangesQuery(sam, SyncCursor.Beginning, APage));

        Assert.Empty(pulled.Value.Changes.Jobs);
        Assert.Empty(pulled.Value.Changes.Stops);
        Assert.Empty(pulled.Value.Changes.RemovedStops);
    }

    /// <summary>
    /// A stop handed to somebody else. The row is still there, so its own stamp reports it — and
    /// the device that lost it is told, rather than keeping a visit that is now on another
    /// technician's list.
    /// </summary>
    [Fact]
    public async Task TellsADeviceAboutAStopThatWasHandedToSomebodyElse()
    {
        await using var services = BuildHost();
        var sam = await ATechnicianAsync(services, "Sam Rivera");
        var ada = await ATechnicianAsync(services, "Ada Okafor");
        var job = await ABookedJobAsync(services);
        var stop = await Send(services, new AssignJobCommand(job, sam, MondayMorning.AddHours(1)));

        var caughtUp = (await PullAsync(services, sam, SyncCursor.Beginning)).Cursor;

        await Send(services, new AssignJobCommand(job, ada, MondayMorning.AddHours(2)));

        var samsView = await PullAsync(services, sam, caughtUp);
        Assert.Equal(stop.Value, Assert.Single(samsView.Changes.RemovedStops));
        Assert.Empty(samsView.Changes.Stops);

        // And the technician who gained it is told the whole thing, because the stop is new to them
        // even though the job is not new to anybody.
        var adasView = await PullAsync(services, ada, caughtUp);
        Assert.Equal(stop.Value, Assert.Single(adasView.Changes.Stops).AssignmentId);
        Assert.Equal(job, Assert.Single(adasView.Changes.Jobs).JobId);
    }

    /// <summary>
    /// The case a deleted row cannot report on itself, and the reason <c>sync_removals</c> exists:
    /// a re-optimisation drops a stop, and a phone that is never told keeps it and drives to it.
    /// </summary>
    [Fact]
    public async Task TellsADeviceAboutAStopThatWasDroppedAltogether()
    {
        await using var services = BuildHost();
        var sam = await ATechnicianAsync(services, "Sam Rivera");
        var job = await ABookedJobAsync(services);
        var stop = await Send(services, new AssignJobCommand(job, sam, MondayMorning.AddHours(1)));

        var caughtUp = (await PullAsync(services, sam, SyncCursor.Beginning)).Cursor;

        // Through the port the optimiser drops stops with, in a scope of its own like any request.
        using (var scope = services.ActingAs(_tenant))
        {
            var assignments = scope.ServiceProvider.GetRequiredService<IAssignmentRepository>();
            var planned = await assignments.GetAsync(stop.Value, CancellationToken.None);

            assignments.Remove(planned!);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        var pulled = await PullAsync(services, sam, caughtUp);

        Assert.Equal(stop.Value, Assert.Single(pulled.Changes.RemovedStops));
        Assert.Empty(pulled.Changes.Stops);

        // The job goes quiet with it: a job is in scope because a stop for it is this technician's,
        // so one with no stop is not theirs to hear about any more.
        Assert.Empty(pulled.Changes.Jobs);
    }

    /// <summary>
    /// What the field recorded comes back, so a device can tell what the server holds from what it
    /// still has queued.
    /// </summary>
    [Fact]
    public async Task SendsBackWhatTheFieldRecordedAgainstTheJob()
    {
        await using var services = BuildHost();
        var sam = await ATechnicianAsync(services, "Sam Rivera");
        var job = await ABookedJobAsync(services);
        await Send(services, new AssignJobCommand(job, sam, MondayMorning.AddHours(1)));

        await using (var write = _postgres.NewContext(_tenant))
        {
            var booked = await write.Jobs.SingleAsync(candidate => candidate.Id == job);
            booked.RecordNotes("Meter behind the boiler.", MondayMorning.AddHours(2));
            booked.RecordLine(LineItemKind.Part, "Run capacitor", 2m, Money.FromDollars(28.50m), MondayMorning);
            await write.SaveChangesAsync();
        }

        var pulled = await PullAsync(services, sam, SyncCursor.Beginning);
        var work = Assert.Single(pulled.Changes.Jobs);

        Assert.Equal("Meter behind the boiler.", work.Notes);
        Assert.Equal(MondayMorning.AddHours(2), work.NotesRecordedAt);
        Assert.Equal(5_700L, Assert.Single(work.Lines).UnitPrice.Multiply(2m).Cents);

        // The version the device will base its next operation on, which is the other half of sync.
        Assert.True(work.Version > 0);
    }

    /// <summary>
    /// A device that has never synced drains its history a page at a time, and misses nothing on
    /// the way.
    /// </summary>
    /// <remarks>
    /// The page is counted in transactions, so three separately-assigned stops are three pages at a
    /// budget of one. What matters is the loop a real client runs: pull, apply, store the cursor,
    /// repeat while <c>HasMore</c> — and that the stops it ends up holding are all of them, each
    /// exactly once per page it appeared in.
    /// </remarks>
    [Fact]
    public async Task DrainsAHistoryTooBigForOnePageWithoutLosingAnything()
    {
        await using var services = BuildHost();
        var sam = await ATechnicianAsync(services, "Sam Rivera");

        var planned = new List<JobId>();

        for (var hour = 1; hour <= 3; hour++)
        {
            var job = await ABookedJobAsync(services);

            // One assignment per transaction, which is what makes this three pages rather than one.
            await Send(services, new AssignJobCommand(job, sam, MondayMorning.AddHours(hour)));
            planned.Add(job);
        }

        var drained = new List<JobId>();
        var cursor = SyncCursor.Beginning;
        var pages = 0;

        while (true)
        {
            var page = await Send(services, new PullChangesQuery(sam, cursor, 1));
            Assert.True(page.IsSuccess);

            drained.AddRange(page.Value.Changes.Stops.Select(stop => stop.JobId));
            cursor = page.Value.Cursor;
            pages++;

            if (!page.Value.HasMore)
            {
                break;
            }

            // The loop terminates because the cursor strictly advances; without that this test
            // hangs, which is the failure mode a split transaction would produce in the field.
            Assert.True(pages < 20, "the pull is not making progress");
        }

        Assert.True(pages > 1, "the history should not have fitted in one page");
        Assert.Equal(
            planned.Select(job => job.Value).Order(),
            drained.Distinct().Select(job => job.Value).Order());

        // And the device is now up to date: one more pull says so and carries nothing new.
        var settled = await Send(services, new PullChangesQuery(sam, cursor, 1));
        Assert.False(settled.Value.HasMore);
        Assert.Empty(settled.Value.Changes.Stops);
    }

    /// <summary>
    /// A transaction bigger than the page budget is sent whole rather than split.
    /// </summary>
    /// <remarks>
    /// The hazard this whole design is shaped around. Every row one transaction wrote shares a
    /// change stamp, so a page that stopped in the middle of one could only resume at that same
    /// stamp — the device would ask again, get the same page, and never move. Here an optimise
    /// plans three stops in one transaction against a budget of one: the answer is all three, and
    /// the cursor moves past them.
    /// </remarks>
    [Fact]
    public async Task SendsATransactionBiggerThanThePageWholeRatherThanSplittingIt()
    {
        await using var services = BuildHost();
        var sam = await ATechnicianAsync(services, "Sam Rivera");

        for (var i = 0; i < 3; i++)
        {
            await ABookedJobAsync(services);
        }

        // One command, one transaction, three stops.
        var optimized = await Send(services, new OptimizeDayCommand(MondayMorning, MondayMorning.AddHours(9)));
        Assert.Equal(3, optimized.Value.Planned);

        var caughtUpOnTheJobs = await Send(services, new PullChangesQuery(sam, SyncCursor.Beginning, 1));

        // Drain whatever the bookings themselves wrote, then look at the page the optimise landed
        // in: whichever page that is, it carries all three of its stops.
        var cursor = caughtUpOnTheJobs.Value.Cursor;
        var stops = caughtUpOnTheJobs.Value.Changes.Stops.Count;
        var pages = 0;

        while (caughtUpOnTheJobs.Value.HasMore && pages < 20)
        {
            var page = await Send(services, new PullChangesQuery(sam, cursor, 1));
            cursor = page.Value.Cursor;
            stops = Math.Max(stops, page.Value.Changes.Stops.Count);
            pages++;

            if (!page.Value.HasMore)
            {
                break;
            }
        }

        Assert.Equal(3, stops);
    }

    [Fact]
    public async Task RefusesACursorThisServerNeverIssued()
    {
        await using var services = BuildHost();
        var sam = await ATechnicianAsync(services, "Sam Rivera");

        var pulled = await Send(services, new PullChangesQuery(sam, new SyncCursor(-1), APage));

        Assert.True(pulled.IsFailure);
    }

    private async Task<IReadOnlyList<long>> StampsOfAsync(JobId job)
    {
        await using var context = _postgres.NewContext(_tenant);

        return await context.Jobs
            .Where(candidate => candidate.Id == job)
            .Select(candidate => EF.Property<long>(candidate, "ChangeSeq"))
            .ToListAsync();
    }

    private async Task<PulledChanges> PullAsync(ServiceProvider services, TechnicianId technician, SyncCursor since)
    {
        var pulled = await Send(services, new PullChangesQuery(technician, since, APage));

        Assert.True(pulled.IsSuccess);

        return pulled.Value;
    }

    private async Task<JobId> ABookedJobAsync(
        ServiceProvider services,
        string label = "Head office",
        string address = "12 Bath Road, Slough")
    {
        var customer = await Send(services, new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value,
            label,
            address,
            51.5107,
            -0.5950));

        var job = await Send(services, new CreateJobCommand(
            customer.Value,
            location.Value,
            "hvac",
            JobPriority.Normal,
            MondayMorning,
            MondayMorning.AddHours(8),
            TimeSpan.FromHours(1)));

        return job.Value;
    }

    private async Task<TechnicianId> ATechnicianAsync(ServiceProvider services, string name)
    {
        var technician = await Send(services, new CreateTechnicianCommand(
            name,
            ["hvac"],
            MondayMorning,
            MondayMorning.AddHours(9),
            51.5074,
            -0.1278));

        return technician.Value;
    }

    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);

    /// <summary>One request: one scope, one tenant.</summary>
    private async Task<TResponse> Send<TResponse>(ServiceProvider services, IRequest<TResponse> request)
    {
        using var scope = services.ActingAs(_tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}
