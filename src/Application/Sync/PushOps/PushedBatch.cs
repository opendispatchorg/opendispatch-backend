using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Sync.PushOps;

/// <summary>
/// What the server made of a pushed batch. Authoritative: the device rebases onto this rather than
/// keeping its own opinion (Document 2 §10).
/// </summary>
/// <param name="Applied">
/// The operations that are in effect, by id — including any the server had already applied before
/// this push. From the device's point of view a re-sent operation that is already in effect
/// succeeded, and telling it otherwise would leave it queueing that operation forever.
/// </param>
/// <param name="Conflicts">The operations that were refused, each with the reason and the sentence for it.</param>
/// <param name="Cursor">
/// Where the device now stands. Taken before this batch commits, so a pull with it returns the
/// batch's own effects — which is the point: what the server holds is the truth, and a device that
/// had an operation refused learns the truth by pulling rather than by being told twice.
/// </param>
/// <remarks>
/// Every operation in the batch is named in exactly one of the two lists, so a device can clear
/// its queue by id and know that nothing was silently dropped. An id sent twice in one batch is
/// answered once — it is one operation, however many times it was written down.
/// </remarks>
public sealed record PushedBatch(
    IReadOnlyList<SyncOpId> Applied,
    IReadOnlyList<SyncOpConflict> Conflicts,
    SyncCursor Cursor);

/// <summary>
/// An operation the server would not apply, and why.
/// </summary>
/// <param name="OpId">Which operation, by the id the device gave it.</param>
/// <param name="Error">
/// What the refusal was. The same <c>Error</c> vocabulary the rest of the application reports
/// with, so a status change refused over sync carries the identical code and sentence it would
/// have carried refused over HTTP.
/// </param>
/// <remarks>
/// A conflict is an ordinary outcome of working offline, not a failure of the push: a technician
/// in a crawlspace cannot know that the office cancelled the job an hour ago. The batch around it
/// still lands, which is why the handler reports these inside a <em>successful</em> result rather
/// than as one.
/// </remarks>
public sealed record SyncOpConflict(SyncOpId OpId, Error Error);
