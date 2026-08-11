using OpenDispatch.Application.Sync;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Sync;

/// <summary>
/// What the op log refuses to record.
/// </summary>
/// <remarks>
/// Only the refusals are worth a test. The record is a statement of fact and has no behaviour to
/// speak of — but a log entry that is missing the part a later reader needs is worse than no
/// entry, because it looks like evidence.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class SyncOpRecordTests
{
    /// <summary>
    /// The one that matters. An empty id is not a degenerate case to tolerate: every operation
    /// sent with a default <see cref="Guid"/> would be the same operation, so the second would be
    /// skipped as a duplicate of work it has nothing to do with — silently, which is exactly how
    /// a technician's afternoon disappears.
    /// </summary>
    [Fact]
    public void RefusesAnOperationWithNoIdOfItsOwn()
    {
        Assert.Throws<ArgumentException>(() => Applied(id: new SyncOpId(Guid.Empty)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesAnOperationThatDoesNotSayWhatItWasDoneTo(string entity)
    {
        Assert.Throws<ArgumentException>(() => Applied(entity: entity));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesAnOperationThatDoesNotSayWhatWasDone(string type)
    {
        Assert.Throws<ArgumentException>(() => Applied(type: type));
    }

    /// <summary>
    /// The column is <c>jsonb</c>, so an operation with nothing to say still says it as JSON.
    /// Empty text is not a payload, it is a payload that went missing on the way here.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesAnOperationWithNoPayload(string payload)
    {
        Assert.Throws<ArgumentException>(() => Applied(payload: payload));
    }

    /// <summary>
    /// A version is a count of how many times a thing has been written. There is no such thing as
    /// having seen it minus once, so a negative base version is a garbled op rather than a stale
    /// one, and recording it would put a number in the log that cannot be compared with anything.
    /// </summary>
    [Fact]
    public void RefusesAnOperationBasedOnAVersionThatCannotExist()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Applied(baseVersion: -1));
    }

    private static SyncOpRecord Applied(
        SyncOpId? id = null,
        string entity = "job",
        string type = "status_change",
        string payload = """{"to":"en_route"}""",
        long baseVersion = 0) =>
        SyncOpRecord.Applied(
            id ?? SyncOpId.From(Guid.NewGuid()),
            OrgId.New(),
            TechnicianId.New(),
            entity,
            entityId: Guid.NewGuid(),
            type,
            payload,
            baseVersion,
            clientTs: DateTimeOffset.UnixEpoch,
            appliedAt: DateTimeOffset.UnixEpoch);
}
