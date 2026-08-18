using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Sync;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Sync;

/// <summary>
/// The op log against a real database: what it keeps, and that one operation can only be in it
/// once.
/// </summary>
/// <remarks>
/// Idempotent replay is the property the whole sync path is built on (TESTING.md), and it comes
/// down to this table. Both halves of the guarantee are tested — the question a push asks before
/// it applies anything, and the primary key that answers for it when two pushes ask at once.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class SyncOpLogTests
{
    private static readonly DateTimeOffset InTheField = new(2027, 3, 1, 9, 14, 0, TimeSpan.FromHours(-5));
    private static readonly DateTimeOffset AtTheServer = new(2027, 3, 1, 16, 2, 0, TimeSpan.Zero);

    private readonly OrgId _tenant = OrgId.New();
    private readonly TechnicianId _technician = TechnicianId.New();
    private readonly PostgresFixture _postgres;

    public SyncOpLogTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>
    /// Everything a device said, kept as it said it — including the payload, which is opaque here
    /// and must survive the round trip unread, and the client's own clock, which is hours behind
    /// the moment the server heard about it.
    /// </summary>
    [Fact]
    public async Task KeepsWhatADeviceSaidItDid()
    {
        var op = StatusChange(SyncOpId.From(Guid.NewGuid()), """{"to":"in_progress","note":"back door"}""");

        await AppendAsync(op);

        await using var read = _postgres.NewContext(_tenant);
        var logged = await read.SyncOps.SingleAsync(entry => entry.Id == op.Id);

        Assert.Equal(_technician, logged.TechnicianId);
        Assert.Equal("job", logged.Entity);
        Assert.Equal(op.EntityId, logged.EntityId);
        Assert.Equal("status_change", logged.Type);
        Assert.Equal(7L, logged.BaseVersion);
        Assert.Equal(InTheField, logged.ClientTs);
        Assert.Equal(AtTheServer, logged.AppliedAt);

        // Through a parse rather than a string comparison: the column is jsonb, so Postgres is
        // entitled to reformat the text. What must survive is the operation's data.
        using var payload = JsonDocument.Parse(logged.Payload);
        Assert.Equal("in_progress", payload.RootElement.GetProperty("to").GetString());
        Assert.Equal("back door", payload.RootElement.GetProperty("note").GetString());
    }

    /// <summary>
    /// The question a push asks before it applies anything: of these operations, which have I
    /// seen? A phone that pushed, lost the response and pushed again is holding a batch that is
    /// part old and part new, and the answer has to separate them in one round trip.
    /// </summary>
    [Fact]
    public async Task ReportsWhichOfABatchHaveAlreadyBeenApplied()
    {
        var alreadyPushed = SyncOpId.From(Guid.NewGuid());
        var alsoAlreadyPushed = SyncOpId.From(Guid.NewGuid());
        var recordedSince = SyncOpId.From(Guid.NewGuid());

        await AppendAsync(StatusChange(alreadyPushed), StatusChange(alsoAlreadyPushed));

        using var scope = _postgres.ActingAs(_tenant);
        var applied = await scope.ServiceProvider
            .GetRequiredService<ISyncOpStore>()
            .FindAppliedAsync([alreadyPushed, recordedSince, alsoAlreadyPushed], CancellationToken.None);

        Assert.Contains(alreadyPushed, applied);
        Assert.Contains(alsoAlreadyPushed, applied);
        Assert.DoesNotContain(recordedSince, applied);
        Assert.Equal(2, applied.Count);
    }

    [Fact]
    public async Task SaysNothingIsAppliedWhenAskedAboutNothing()
    {
        using var scope = _postgres.ActingAs(_tenant);
        var applied = await scope.ServiceProvider
            .GetRequiredService<ISyncOpStore>()
            .FindAppliedAsync([], CancellationToken.None);

        Assert.Empty(applied);
    }

    /// <summary>
    /// The backstop under the check above. Two pushes of the same batch can both look before
    /// either writes, so the log cannot rely on being asked — the key refuses the second write
    /// whatever the application believed.
    /// </summary>
    [Fact]
    public async Task RefusesASecondRecordOfTheSameOperation()
    {
        var pushedTwice = SyncOpId.From(Guid.NewGuid());

        await AppendAsync(StatusChange(pushedTwice));

        using var scope = _postgres.ActingAs(_tenant);
        scope.ServiceProvider.GetRequiredService<ISyncOpStore>().Add(StatusChange(pushedTwice));

        await Assert.ThrowsAsync<DuplicateRecordException>(() => scope.ServiceProvider
            .GetRequiredService<IUnitOfWork>()
            .SaveChangesAsync(CancellationToken.None));
    }

    /// <summary>
    /// The log is tenant-scoped like everything else, which matters more here than it looks: an
    /// unfiltered log would let one organization's device be told that an operation it has never
    /// sent was already applied.
    /// </summary>
    [Fact]
    public async Task KeepsOneTenantsOperationsOutOfAnothersSight()
    {
        var op = SyncOpId.From(Guid.NewGuid());

        await AppendAsync(StatusChange(op));

        using var elsewhere = _postgres.ActingAs(OrgId.New());
        var applied = await elsewhere.ServiceProvider
            .GetRequiredService<ISyncOpStore>()
            .FindAppliedAsync([op], CancellationToken.None);

        Assert.Empty(applied);
    }

    private async Task AppendAsync(params SyncOpRecord[] ops)
    {
        using var scope = _postgres.ActingAs(_tenant);
        var store = scope.ServiceProvider.GetRequiredService<ISyncOpStore>();

        foreach (var op in ops)
        {
            store.Add(op);
        }

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(CancellationToken.None);
    }

    private SyncOpRecord StatusChange(SyncOpId id, string payload = """{"to":"en_route"}""") =>
        SyncOpRecord.Applied(
            id,
            _tenant,
            _technician,
            entity: "job",
            entityId: Guid.NewGuid(),
            type: "status_change",
            payload: payload,
            baseVersion: 7,
            clientTs: InTheField,
            appliedAt: AtTheServer);
}
