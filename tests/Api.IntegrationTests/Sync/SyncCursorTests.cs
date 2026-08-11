using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Sync;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Sync;

/// <summary>
/// The change cursor: the one guarantee a device's whole picture of the day rests on.
/// </summary>
/// <remarks>
/// <para>
/// The promise is that everything a device has not been told about is at or after the cursor it
/// holds. Getting it wrong does not break loudly — it leaves a technician driving to a job that
/// was cancelled hours ago, and nothing anywhere reports an error. So the interesting test here
/// is not that a cursor advances; it is that it refuses to advance past a change that is still
/// being written.
/// </para>
/// <para>
/// What "changed since a cursor" returns is step 43's business. These tests only prove the
/// mechanism underneath it: that rows are stamped, that edits re-stamp them, and that the
/// watermark never runs ahead of a transaction in flight.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class SyncCursorTests
{
    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public SyncCursorTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>
    /// A row written after a cursor was taken is at or after it, and the cursor then moves past
    /// it — which together are what let a device ask twice and be told about the change once.
    /// </summary>
    [Fact]
    public async Task StampsARowSoThatACursorTakenBeforeItWouldFindIt()
    {
        var before = await CursorAsync();
        var job = await BookAsync();

        var stamp = await ChangeStampOfAsync(job.Id);
        var after = await CursorAsync();

        Assert.True(stamp >= before.Value, $"a row written after cursor {before} was stamped {stamp}");
        Assert.True(after.Value > before.Value, $"the cursor did not move: {before} then {after}");
    }

    /// <summary>
    /// An edit re-stamps the row. Without this the log would only ever describe rows that had
    /// just been created, and a job whose status moved — the most ordinary thing that happens all
    /// day — would never reach the phone holding it.
    /// </summary>
    [Fact]
    public async Task RestampsARowThatIsEdited()
    {
        var job = await BookAsync();
        var whenBooked = await ChangeStampOfAsync(job.Id);

        var beforeTheEdit = await CursorAsync();

        await using (var write = _postgres.NewContext(_tenant))
        {
            var booked = await write.Jobs.SingleAsync(row => row.Id == job.Id);
            booked.Schedule();
            await write.SaveChangesAsync();
        }

        var whenScheduled = await ChangeStampOfAsync(job.Id);

        Assert.True(whenScheduled > whenBooked, $"an edit left the stamp at {whenBooked}");
        Assert.True(
            whenScheduled >= beforeTheEdit.Value,
            $"an edit after cursor {beforeTheEdit} was stamped {whenScheduled}");
    }

    /// <summary>
    /// The property the mechanism exists for. A cursor is handed out while another transaction is
    /// midway through writing a job; the device that stores that cursor must still be told about
    /// that job when it asks again.
    /// </summary>
    /// <remarks>
    /// This is what a stamp allocated <em>before</em> the write commits cannot do, and why the
    /// value comes from the writing transaction's own id rather than from a counter somebody
    /// increments on the way past. Written with two connections because that is the only way the
    /// failure happens: one that is holding a transaction open, and one being asked for a cursor
    /// while it does.
    /// </remarks>
    [Fact]
    public async Task DoesNotRunAheadOfAChangeThatIsStillBeingWritten()
    {
        var job = JobBuilder.Any().ForOrg(_tenant).Build();

        await using var writer = _postgres.NewContext(_tenant);
        await using var transaction = await writer.Database.BeginTransactionAsync();

        writer.Jobs.Add(job);
        await writer.SaveChangesAsync();

        // Taken on a different connection, so it sees the database as everyone else does: the row
        // above does not exist yet.
        var duringTheWrite = await CursorAsync();

        await using (var elsewhere = _postgres.NewContext(_tenant))
        {
            Assert.False(await elsewhere.Jobs.AnyAsync(row => row.Id == job.Id));
        }

        await transaction.CommitAsync();

        await using var read = _postgres.NewContext(_tenant);
        var changed = await read.Jobs
            .Where(row => EF.Property<long>(row, "ChangeSeq") >= duringTheWrite.Value)
            .Select(row => row.Id)
            .ToListAsync();

        Assert.Contains(job.Id, changed);
    }

    /// <summary>
    /// Every table carries the stamp, the op log included — so a device can be told about an
    /// operation the office rejected as easily as about a job it moved.
    /// </summary>
    [Fact]
    public async Task StampsTheOpLogToo()
    {
        var before = await CursorAsync();
        var op = SyncOpRecord.Applied(
            SyncOpId.From(Guid.NewGuid()),
            _tenant,
            TechnicianId.New(),
            entity: "job",
            entityId: Guid.NewGuid(),
            type: "add_note",
            payload: """{"text":"meter in the cupboard"}""",
            baseVersion: 0,
            clientTs: DateTimeOffset.UnixEpoch,
            appliedAt: DateTimeOffset.UnixEpoch);

        using (var scope = _postgres.ActingAs(_tenant))
        {
            scope.ServiceProvider.GetRequiredService<ISyncOpStore>().Add(op);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
        }

        await using var read = _postgres.NewContext(_tenant);
        var stamp = await read.SyncOps
            .Where(entry => entry.Id == op.Id)
            .Select(entry => EF.Property<long>(entry, "ChangeSeq"))
            .SingleAsync();

        Assert.True(stamp >= before.Value, $"an op recorded after cursor {before} was stamped {stamp}");
    }

    private async Task<SyncCursor> CursorAsync()
    {
        using var scope = _postgres.ActingAs(_tenant);

        return await scope.ServiceProvider
            .GetRequiredService<ISyncCursorSource>()
            .CurrentAsync(CancellationToken.None);
    }

    private async Task<Job> BookAsync()
    {
        var job = JobBuilder.Any().ForOrg(_tenant).Build();

        await using var write = _postgres.NewContext(_tenant);
        write.Jobs.Add(job);
        await write.SaveChangesAsync();

        return job;
    }

    private async Task<long> ChangeStampOfAsync(JobId id)
    {
        await using var read = _postgres.NewContext(_tenant);

        return await read.Jobs
            .Where(row => row.Id == id)
            .Select(row => EF.Property<long>(row, "ChangeSeq"))
            .SingleAsync();
    }
}
