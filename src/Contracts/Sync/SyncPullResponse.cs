namespace OpenDispatch.Contracts.Sync;

/// <summary>
/// Everything that changed in a device's world since the cursor it asked with, and where it
/// now stands. The body of <c>GET /sync/pull?since={cursor}</c>.
/// </summary>
/// <remarks>
/// <para>
/// Pulling twice with the same cursor returns the same window, so a response lost to a dropped
/// connection costs a repeat rather than a gap. The device only advances by sending back the
/// cursor it was given.
/// </para>
/// <para>
/// <strong>A pull is a page, not the whole world.</strong> A device that has never synced — or one
/// that has been off for a month — would otherwise be handed its entire history in one response
/// over a phone connection. <see cref="HasMore"/> is how it knows to go round again: pull, apply,
/// store the cursor, and repeat while it is <see langword="true"/>.
/// </para>
/// </remarks>
/// <param name="Changes">The entities that moved, as the server now holds them.</param>
/// <param name="Cursor">
/// The watermark to ask with next time. Opaque — the shape of the server's bookmark is not
/// part of the contract.
/// </param>
/// <param name="HasMore">
/// Whether the server stopped early and has more waiting. When <see langword="true"/>, pull again
/// with <paramref name="Cursor"/> as soon as this page is applied; when <see langword="false"/>,
/// the device is up to date as of <paramref name="Cursor"/>.
/// </param>
public sealed record SyncPullResponse(IReadOnlyList<SyncChange> Changes, string Cursor, bool HasMore);
