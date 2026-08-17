using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Sync;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Tests.Sync;

/// <summary>
/// <see cref="ISyncOpStore"/> over a <see cref="FakeStore{TAggregate}"/>, scoped to a tenant like
/// the real one.
/// </summary>
/// <remarks>
/// The staged/written split is what makes it worth having here rather than a list: the real store
/// answers <see cref="FindAppliedAsync"/> from what is committed, so an operation recorded earlier
/// in the same uncommitted batch is invisible to it. A fake that answered from staged writes would
/// let the handler lean on the database for something it has to remember itself.
/// </remarks>
internal sealed class FakeSyncOpStore(FakeStore<SyncOpRecord> store, ITenantContext tenant) : ISyncOpStore
{
    public Task<IReadOnlySet<SyncOpId>> FindAppliedAsync(
        IReadOnlyCollection<SyncOpId> ids,
        CancellationToken ct) =>
        Task.FromResult<IReadOnlySet<SyncOpId>>(
            store.Owned(tenant.OrgId).Select(op => op.Id).Where(ids.Contains).ToHashSet());

    public void Add(SyncOpRecord op) => store.Stage(op);
}

/// <summary>
/// A change reader that answers with nothing and remembers when it was asked.
/// </summary>
/// <remarks>
/// It returns an empty scope because what pull reads is a question about SQL and is tested against
/// Postgres. What it cannot be tested against Postgres is the handler's one decision — that the
/// cursor is taken <em>before</em> the read — because the failure it prevents is a change committed
/// in the gap between them, which no in-process test can arrange. So the fakes record the order and
/// the test asserts it.
/// </remarks>
internal sealed class RecordingChangeReader(CallOrder order) : ISyncChangeReader
{
    /// <summary>The cursor the last read was asked with.</summary>
    public SyncCursor Since { get; private set; } = SyncCursor.Beginning;

    /// <summary>Who the last read was for.</summary>
    public TechnicianId Technician { get; private set; }

    /// <summary>The page budget the last read was given.</summary>
    public int MaxTransactions { get; private set; }

    /// <summary>The row budget the last read was given.</summary>
    public int MaxRows { get; private set; }

    /// <summary>
    /// What the next read reports as the last whole transaction in its page — null for "that was
    /// everything", which is what a reader with nothing left to give answers.
    /// </summary>
    public long? Ceiling { get; set; }

    public Task<SyncScopePage> ReadAsync(
        TechnicianId technician,
        SyncCursor since,
        int maxTransactions,
        int maxRows,
        CancellationToken ct)
    {
        Since = since;
        Technician = technician;
        MaxTransactions = maxTransactions;
        MaxRows = maxRows;
        order.Record(nameof(ISyncChangeReader));

        return Task.FromResult(new SyncScopePage(new SyncScopeChanges([], [], []), Ceiling));
    }
}

/// <summary>What was called, in the order it was called.</summary>
internal sealed class CallOrder
{
    private readonly List<string> _calls = [];

    /// <summary>The ports that have been called, oldest first.</summary>
    public IReadOnlyList<string> Calls => _calls;

    /// <summary>Notes that a port was called.</summary>
    public void Record(string port) => _calls.Add(port);
}

/// <summary>
/// A cursor source that hands out a number a test can read back, and moves it on every call.
/// </summary>
/// <remarks>
/// Moving matters: the handler must ask for the cursor rather than carry one from earlier in the
/// request, and a constant would agree with either. What the real source guarantees — that the
/// watermark never runs ahead of a change still being written — is a promise about Postgres and is
/// tested against Postgres.
/// </remarks>
internal sealed class SteppingCursors(CallOrder order) : ISyncCursorSource
{
    private long _watermark = 1_000;

    /// <summary>The last cursor handed out.</summary>
    public SyncCursor Issued { get; private set; } = SyncCursor.Beginning;

    public Task<SyncCursor> CurrentAsync(CancellationToken ct)
    {
        Issued = new SyncCursor(++_watermark);
        order.Record(nameof(ISyncCursorSource));

        return Task.FromResult(Issued);
    }
}
