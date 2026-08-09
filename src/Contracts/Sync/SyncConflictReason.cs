using System.Text.Json.Serialization;

namespace OpenDispatch.Contracts.Sync;

/// <summary>
/// The kinds of refusal a pushed operation can meet.
/// </summary>
/// <remarks>
/// <para>
/// Two, because the conflict policy has two halves (Document 2 §10): status is governed by
/// the state machine, and everything else is governed by versions. An operation that had
/// already been applied is not here — that is idempotency working, and the device is told it
/// succeeded.
/// </para>
/// <para>
/// The two are separate because a client should treat them differently. An illegal transition
/// is final and the device's copy of the job is simply wrong; a version conflict means
/// somebody else got there first, and the technician may well want to do it again once they
/// have seen the newer state.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SyncConflictReason>))]
public enum SyncConflictReason
{
    /// <summary>
    /// The status change is not one the job's state machine allows from where the job
    /// actually is. Legality is the rule, not who wrote last — a stale phone cannot push a
    /// cancelled job back into progress by being the most recent writer.
    /// </summary>
    IllegalTransition = 0,

    /// <summary>
    /// The entity has moved on since the version the device based its operation on. What the
    /// server holds stands, and the device rebases onto it.
    /// </summary>
    VersionConflict = 1,
}
