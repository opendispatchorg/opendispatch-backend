using System.Text.Json;
using OpenDispatch.Contracts.Board;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Contracts.Tests;

/// <summary>
/// The board events as they leave the hub and arrive in a browser. These are the shapes
/// Document 2 §11 singles out as the ones that break silently — no OpenAPI document
/// describes a SignalR message, so nothing but a test stands between a renamed field here
/// and a board that quietly stops updating.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class BoardEventWireTests
{
    /// <summary>
    /// What ASP.NET Core and SignalR's JSON hub protocol both do by default, so these tests
    /// assert the encoding the clients will actually be handed rather than one of our own.
    /// </summary>
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ABoardEventReadsTheWayAClientExpects()
    {
        var updated = new JobUpdated(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff"), JobStatus.EnRoute, 4);

        var json = JsonSerializer.Serialize(updated, Wire);

        // camelCase fields and a status a person can read. The status is the one that costs
        // something to get wrong: without its converter it goes out as 3, and a TypeScript
        // client comparing strings would match nothing at all while looking perfectly fine.
        Assert.Equal(
            """{"jobId":"6f9619ff-8b86-d011-b42d-00c04fc964ff","status":"EnRoute","version":4}""",
            json);
    }

    [Fact]
    public void JobUpdatedSurvivesTheRoundTrip()
    {
        var updated = new JobUpdated(Guid.NewGuid(), JobStatus.Completed, 12);

        Assert.Equal(updated, RoundTrip(updated));
    }

    [Fact]
    public void AssignmentUpdatedSurvivesTheRoundTrip()
    {
        var updated = new AssignmentUpdated(
            AssignmentId: Guid.NewGuid(),
            JobId: Guid.NewGuid(),
            TechnicianId: Guid.NewGuid(),
            Sequence: 3,
            ScheduledStart: new DateTimeOffset(2026, 4, 17, 14, 30, 0, TimeSpan.FromHours(-5)),
            TravelMin: 12.5,
            Version: 9);

        var read = RoundTrip(updated);

        // The offset travels with the time. A stop planned for half past two in Chicago that
        // came back as half past two UTC would land five lanes-widths away on the board.
        Assert.Equal(updated, read);
        Assert.Equal(updated.ScheduledStart.Offset, read.ScheduledStart.Offset);
    }

    [Fact]
    public void TechnicianMovedSurvivesTheRoundTrip()
    {
        var moved = new TechnicianMoved(
            Guid.NewGuid(),
            Lat: 41.8781,
            Lng: -87.6298,
            At: new DateTimeOffset(2026, 4, 17, 9, 5, 12, TimeSpan.Zero));

        Assert.Equal(moved, RoundTrip(moved));
    }

    private static T RoundTrip<T>(T value)
    {
        var read = JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Wire), Wire);

        Assert.NotNull(read);

        return read;
    }
}
