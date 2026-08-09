namespace OpenDispatch.Contracts.Sync;

/// <summary>
/// A device emptying its queue: everything it did since it last got through, in the order it
/// did it. The body of <c>POST /sync/push</c>.
/// </summary>
/// <remarks>
/// A batch rather than an operation per request because the connection is the scarce thing —
/// a technician coming out of a basement has one moment of signal and a dozen actions to
/// report. Order is meaningful: the ops are replayed as the technician performed them, so a
/// job that was started and then completed is not judged as a jump from dispatched to done.
/// Re-sending the same batch is safe; each op carries its own idempotency key.
/// </remarks>
/// <param name="Ops">The operations, oldest first.</param>
public sealed record SyncPushRequest(IReadOnlyList<SyncOp> Ops);
