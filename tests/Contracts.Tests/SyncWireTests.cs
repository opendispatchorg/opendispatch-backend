using System.Text.Json;
using OpenDispatch.Contracts.Sync;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Contracts.Tests;

/// <summary>
/// The sync envelopes as a phone writes and reads them. Like the board events these are
/// invisible to OpenAPI, and unlike the board events they are what a technician's day depends
/// on after eight hours with no signal.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class SyncWireTests
{
    /// <summary>
    /// What ASP.NET Core does by default, so these tests assert the encoding a device will
    /// actually be handed rather than one of our own.
    /// </summary>
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    private static readonly Guid OpId = Guid.Parse("2a2f4b8e-3e4c-4d6f-8a1b-9c0d1e2f3a4b");
    private static readonly Guid JobId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");

    [Fact]
    public void AnOpSurvivesTheRoundTripAndItsPayloadOutlivesWhatItCameFrom()
    {
        string json;

        using (var payload = JsonDocument.Parse("""{"to":"InProgress","note":"customer let me in"}"""))
        {
            json = JsonSerializer.Serialize(
                new SyncOp(
                    OpId,
                    "job",
                    JobId,
                    "status_change",
                    payload.RootElement,
                    BaseVersion: 7,
                    ClientTs: new DateTimeOffset(2026, 4, 17, 8, 42, 31, TimeSpan.FromHours(-5))),
                Wire);
        }

        var read = JsonSerializer.Deserialize<SyncOp>(json, Wire);

        Assert.NotNull(read);
        Assert.Equal(OpId, read.Id);
        Assert.Equal("job", read.Entity);
        Assert.Equal(JobId, read.EntityId);
        Assert.Equal("status_change", read.Type);
        Assert.Equal(7, read.BaseVersion);
        Assert.Equal(new DateTimeOffset(2026, 4, 17, 8, 42, 31, TimeSpan.FromHours(-5)), read.ClientTs);

        // The document these came from was disposed two lines after it was parsed. A payload
        // that still pointed into it would throw here — which is the trap waiting for anything
        // that holds an op past the request that carried it, as the push handler must.
        Assert.Equal("InProgress", read.Payload.GetProperty("to").GetString());
        Assert.Equal("customer let me in", read.Payload.GetProperty("note").GetString());
    }

    [Fact]
    public void ABatchKeepsItsOpsInTheOrderTheyWereDone()
    {
        using var payload = JsonDocument.Parse("""{"to":"EnRoute"}""");

        var request = new SyncPushRequest(
        [
            new SyncOp(OpId, "job", JobId, "status_change", payload.RootElement, 7, DateTimeOffset.UnixEpoch),
            new SyncOp(Guid.NewGuid(), "job", JobId, "add_note", payload.RootElement, 8, DateTimeOffset.UnixEpoch),
        ]);

        var read = JsonSerializer.Deserialize<SyncPushRequest>(JsonSerializer.Serialize(request, Wire), Wire);

        // Replay order is the whole reason a batch is a list: started-then-completed must not
        // arrive as a jump from dispatched to done.
        Assert.NotNull(read);
        Assert.Equal(["status_change", "add_note"], read.Ops.Select(op => op.Type));
    }

    [Fact]
    public void TheAnswerToAPushNamesItsConflictsInWords()
    {
        var response = new SyncPushResponse(
            [OpId],
            [new SyncConflict(Guid.Parse("11112222-3333-4444-5555-666677778888"), SyncConflictReason.VersionConflict, "Somebody else got there first.")],
            "42");

        var json = JsonSerializer.Serialize(response, Wire);
        var read = JsonSerializer.Deserialize<SyncPushResponse>(json, Wire);

        // A reason a client branches on should not be an integer it has to look up, and the
        // read-only collections have to come back as collections rather than nothing at all.
        Assert.Contains("\"reason\":\"VersionConflict\"", json, StringComparison.Ordinal);
        Assert.NotNull(read);
        Assert.Equal([OpId], read.Applied);
        Assert.Equal(SyncConflictReason.VersionConflict, Assert.Single(read.Conflicts).Reason);
        Assert.Equal("42", read.Cursor);
    }

    [Fact]
    public void APullCanSayThatSomethingIsGoneAsWellAsWhatItIsNow()
    {
        using var state = JsonDocument.Parse("""{"status":"Dispatched"}""");

        var response = new SyncPullResponse(
            [
                new SyncChange("job", JobId, 9, state.RootElement, Deleted: false),
                new SyncChange("assignment", OpId, 4, State: null, Deleted: true),
            ],
            "43",
            HasMore: false);

        var read = JsonSerializer.Deserialize<SyncPullResponse>(JsonSerializer.Serialize(response, Wire), Wire);

        Assert.NotNull(read);
        Assert.Equal("Dispatched", read.Changes[0].State?.GetProperty("status").GetString());
        Assert.False(read.Changes[0].Deleted);

        // A stop re-optimised onto somebody else's day has to be sayable. A phone that is
        // never told keeps it and drives to it.
        Assert.True(read.Changes[1].Deleted);
        Assert.Null(read.Changes[1].State);
        Assert.Equal("43", read.Cursor);
    }
}
