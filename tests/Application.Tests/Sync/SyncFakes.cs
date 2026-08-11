using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Sync;
using OpenDispatch.Application.Tests.Fakes;

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
/// A cursor source that hands out a number a test can read back, and moves it on every call.
/// </summary>
/// <remarks>
/// Moving matters: the handler must ask for the cursor rather than carry one from earlier in the
/// request, and a constant would agree with either. What the real source guarantees — that the
/// watermark never runs ahead of a change still being written — is a promise about Postgres and is
/// tested against Postgres.
/// </remarks>
internal sealed class SteppingCursors : ISyncCursorSource
{
    private long _watermark = 1_000;

    /// <summary>The last cursor handed out.</summary>
    public SyncCursor Issued { get; private set; } = SyncCursor.Beginning;

    public Task<SyncCursor> CurrentAsync(CancellationToken ct)
    {
        Issued = new SyncCursor(++_watermark);

        return Task.FromResult(Issued);
    }
}
