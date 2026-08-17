using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Application.Sync;
using OpenDispatch.Application.Sync.PullChanges;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Sync;

/// <summary>
/// The handler's one decision, and the two shapes it refuses.
/// </summary>
/// <remarks>
/// What pull actually reads is a question about SQL — the window, the scope, the removals — and is
/// tested against Postgres in <c>PullChangesFlowTests</c>, because a fake would be asserting
/// arithmetic written in the test itself. What is left here is the ordering, which no integration
/// test can pin down: the failure it prevents happens in the gap between two statements, and
/// arranging a commit inside that gap is not something an in-process test can do.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class PullChangesTests
{
    /// <summary>A page budget big enough that nothing these tests write is ever capped.</summary>
    private const int APage = 200;

    /// <summary>
    /// A row budget no test here is about: high enough that only the transaction budget ever
    /// stops a page, so these tests keep saying what they were written to say.
    /// </summary>
    private const int AnyRows = 100_000;

    /// <summary>
    /// Taken after the read, a cursor would sit above a change committed while the read was
    /// running — and a change below a device's cursor is never sent again, so the stop cancelled at
    /// that moment is one the technician drives to. The order is the whole handler.
    /// </summary>
    [Fact]
    public async Task TakesTheCursorBeforeReadingWhatChanged()
    {
        await using var slice = SliceHost.Sync();

        await slice.Send(new PullChangesQuery(TechnicianId.New(), SyncCursor.Beginning, APage, AnyRows));

        Assert.Equal(
            [nameof(ISyncCursorSource), nameof(ISyncChangeReader)],
            slice.Fake<CallOrder>().Calls);
    }

    [Fact]
    public async Task ReadsForTheTechnicianAskedAboutAndHandsBackTheCursorItTook()
    {
        await using var slice = SliceHost.Sync();
        var sam = TechnicianId.New();
        var since = new SyncCursor(4_096);

        var pulled = await slice.Send(new PullChangesQuery(sam, since, APage, AnyRows));

        var reader = slice.Fake<RecordingChangeReader>();
        Assert.Equal(sam, reader.Technician);
        Assert.Equal(since, reader.Since);
        Assert.Equal(slice.Fake<SteppingCursors>().Issued, pulled.Value.Cursor);
    }

    /// <summary>
    /// A cursor the edge would never have produced — <c>SyncCursor.TryParse</c> refuses anything
    /// this server did not issue — means a caller built the value itself. Reading it as "from the
    /// beginning" would answer a bug with a full resync of the technician's history.
    /// </summary>
    [Fact]
    public async Task RefusesACursorThisServerNeverIssued()
    {
        await using var slice = SliceHost.Sync();

        var pulled = await slice.Send(new PullChangesQuery(TechnicianId.New(), new SyncCursor(-1), APage, AnyRows));

        Assert.IsType<ValidationError>(pulled.Error);
    }

    [Fact]
    public async Task RefusesAPullThatDoesNotSayWhoseWorkItIsFor()
    {
        await using var slice = SliceHost.Sync();

        var pulled = await slice.Send(new PullChangesQuery(default, SyncCursor.Beginning, APage, AnyRows));

        Assert.IsType<ValidationError>(pulled.Error);
    }
}
