using System.Text.Json;

namespace OpenDispatch.Contracts.Sync;

/// <summary>
/// One entity as the server currently holds it, sent to a device that is behind.
/// </summary>
/// <remarks>
/// <para>
/// A whole state rather than a diff. A device that has been offline for a day has no useful
/// base to apply a diff to, and the server's copy being the one that wins is the entire
/// conflict policy — handing over the answer rather than the steps to reach it is what makes
/// rebasing a replacement instead of a merge.
/// </para>
/// <para>
/// <see cref="Version"/> is what the device puts in the next operation it bases on this, so
/// the two halves of sync close: what came back from a pull is what a later push is judged
/// against.
/// </para>
/// </remarks>
/// <param name="Entity">What kind of thing this is — the same vocabulary an operation uses.</param>
/// <param name="EntityId">Which one.</param>
/// <param name="Version">Its concurrency stamp as the server holds it.</param>
/// <param name="State">
/// The entity as the client should now hold it, or <see langword="null"/> when
/// <paramref name="Deleted"/> is set — the two always agree.
/// </param>
/// <param name="Deleted">
/// The entity is gone from this device's world: cancelled, or a stop re-optimised onto
/// somebody else's day. Removal has to be sayable, because a phone that is never told simply
/// keeps the stop and drives to it.
/// </param>
public sealed record SyncChange(
    string Entity,
    Guid EntityId,
    long Version,
    JsonElement? State,
    bool Deleted);
