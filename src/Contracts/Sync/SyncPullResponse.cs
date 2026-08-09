namespace OpenDispatch.Contracts.Sync;

/// <summary>
/// Everything that changed in a device's world since the cursor it asked with, and where it
/// now stands. The body of <c>GET /sync/pull?since={cursor}</c>.
/// </summary>
/// <remarks>
/// Pulling twice with the same cursor returns the same window, so a response lost to a dropped
/// connection costs a repeat rather than a gap. The device only advances by sending back the
/// cursor it was given.
/// </remarks>
/// <param name="Changes">The entities that moved, as the server now holds them.</param>
/// <param name="Cursor">
/// The watermark to ask with next time. Opaque — the shape of the server's bookmark is not
/// part of the contract.
/// </param>
public sealed record SyncPullResponse(IReadOnlyList<SyncChange> Changes, string Cursor);
