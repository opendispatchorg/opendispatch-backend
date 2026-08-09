namespace OpenDispatch.Contracts.Sync;

/// <summary>
/// What the server made of a pushed batch. Authoritative: the device rebases onto this rather
/// than keeping its own opinion (Document 2 §10).
/// </summary>
/// <remarks>
/// <para>
/// Every op in the batch comes back in exactly one of the two lists, so a device can clear
/// its queue by id and know that nothing was silently dropped.
/// </para>
/// <para>
/// A conflict says what the server refused and why, not what the truth now is. The device
/// learns that by pulling with <see cref="Cursor"/>, which is returned on every push for that
/// reason — the round trip is one request, and it keeps a single description of an entity's
/// current state on the pull path instead of two that can disagree.
/// </para>
/// </remarks>
/// <param name="Applied">
/// The ops that landed, by id. An op the server had already applied is reported here too:
/// from the device's point of view a re-sent operation that is already in effect succeeded,
/// and telling it otherwise would leave it queueing the op forever.
/// </param>
/// <param name="Conflicts">The ops that were refused, each with a reason fit to show a technician.</param>
/// <param name="Cursor">
/// Where the device now stands in the change stream. Opaque — it is the server's watermark,
/// and a client that parses it is reading something it was not promised.
/// </param>
public sealed record SyncPushResponse(
    IReadOnlyList<Guid> Applied,
    IReadOnlyList<SyncConflict> Conflicts,
    string Cursor);
