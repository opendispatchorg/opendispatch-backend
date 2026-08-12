using System.Text.Json.Serialization;

namespace OpenDispatch.Contracts.Sync;

/// <summary>
/// The kinds of refusal a pushed operation can meet.
/// </summary>
/// <remarks>
/// <para>
/// Two of these are the conflict policy's two halves (Document 2 §10): status is governed by
/// the state machine, and everything else is governed by versions. An operation that had
/// already been applied is not here — that is idempotency working, and the device is told it
/// succeeded.
/// </para>
/// <para>
/// They are separate because a client should treat them differently. An illegal transition
/// is final and the device's copy of the job is simply wrong; a version conflict means
/// somebody else got there first, and the technician may well want to do it again once they
/// have seen the newer state.
/// </para>
/// <para>
/// <see cref="Unsupported"/> is the third, and it is here because the alternative is a poisoned
/// queue. <c>SyncOp.Entity</c> and <c>SyncOp.Type</c> are strings precisely so a device can name
/// an operation this server has never heard of; without a way to refuse exactly that operation,
/// the only answer left is to reject the whole batch, and a device that keeps re-sending the
/// batch it cannot get rid of never syncs the rest of the technician's day either.
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

    /// <summary>
    /// The server cannot apply this operation at all: it does not know that kind of operation,
    /// or cannot read its payload, or does not have the thing it names.
    /// </summary>
    /// <remarks>
    /// One reason rather than three, because a device does the same thing with all of them —
    /// drop the operation, because retrying it will fail identically forever. The message says
    /// which, for the technician and for whoever reads the logs. Unlike the other two, this one
    /// says nothing about the state of the world: it is the server declining to guess.
    /// </remarks>
    Unsupported = 2,
}
