using System.Text.Json;

namespace OpenDispatch.Contracts.Sync;

/// <summary>
/// One thing a technician did in the field: started a job, added a note, added a part,
/// finished. Queued on the device and pushed in batches (Document 2 §10).
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Entity"/> and <see cref="Type"/> are strings rather than enums on purpose. The
/// set of operations grows with the app, and a phone that has not been updated for a month
/// must still be able to push what it recorded — while a server one release ahead of a device
/// must be able to name an operation that device has never heard of. An enum would turn both
/// into a deserialization failure that loses the whole batch instead of one op the server can
/// report on.
/// </para>
/// <para>
/// Two things about <see cref="Payload"/> are worth knowing. It is opaque here — its shape
/// depends on <see cref="Type"/> and is the push handler's business — and a
/// <see cref="JsonElement"/> that was never given a value cannot be written back out at all,
/// so a batch arriving without one is rejected at the edge rather than blowing up later.
/// Record equality is likewise not to be relied on: <see cref="JsonElement"/> has no value
/// equality, so two ops with identical payloads are not equal. Compare
/// <see cref="JsonElement.GetRawText"/> when that is what is meant.
/// </para>
/// </remarks>
/// <param name="Id">
/// The device's own identifier for this operation, and the idempotency key. A phone that
/// pushes, loses signal, and pushes again sends the same id, which is how the server knows
/// not to apply it twice.
/// </param>
/// <param name="Entity">What kind of thing it happened to — <c>"job"</c>, <c>"line_item"</c>.</param>
/// <param name="EntityId">Which one.</param>
/// <param name="Type">What was done — <c>"status_change"</c>, <c>"add_note"</c>.</param>
/// <param name="Payload">The operation's own data, shaped by <paramref name="Type"/>.</param>
/// <param name="BaseVersion">
/// The version of the entity the device was looking at when it acted. What a stale write is
/// judged against; the server's answer is authoritative either way.
/// </param>
/// <param name="ClientTs">
/// When it happened on the device, which is not when it arrived. The ordering a technician
/// would recognise, and what last-write-wins on free text is decided by.
/// </param>
public sealed record SyncOp(
    Guid Id,
    string Entity,
    Guid EntityId,
    string Type,
    JsonElement Payload,
    long BaseVersion,
    DateTimeOffset ClientTs);
