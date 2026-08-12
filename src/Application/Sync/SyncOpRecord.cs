using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Sync;

/// <summary>
/// One operation from a technician's device, recorded as applied. The op log (Document 2 §10).
/// </summary>
/// <remarks>
/// <para>
/// This is what makes a push idempotent. A phone that pushes, loses signal on the response and
/// pushes again sends the same <see cref="Id"/>; finding that id here is how the server knows
/// the work was already done, rather than starting a job twice or billing an hour twice.
/// </para>
/// <para>
/// It is not an aggregate and deliberately not in the domain. A job's lifecycle is a business
/// rule; a record of what a device said and when it was believed is a fact about the protocol,
/// with no invariant of its own beyond being complete. It carries no <c>Version</c> for the same
/// reason: a row that is written once and never edited has nothing to be concurrent about.
/// </para>
/// <para>
/// Only applied operations are recorded. An op the server refused — an illegal transition from a
/// stale phone — leaves no row, so the answer to resending it is worked out again from the state
/// of the job rather than remembered from the first refusal.
/// </para>
/// </remarks>
public sealed class SyncOpRecord
{
    // Materialisation constructor — see the note on Job. The placeholders are overwritten from
    // the row before anything can observe them.
    private SyncOpRecord()
    {
        Entity = string.Empty;
        Type = string.Empty;
        Payload = string.Empty;
    }

    private SyncOpRecord(
        SyncOpId id,
        OrgId orgId,
        TechnicianId technicianId,
        string entity,
        Guid entityId,
        string type,
        string payload,
        long baseVersion,
        DateTimeOffset clientTs,
        DateTimeOffset appliedAt)
    {
        Id = id;
        OrgId = orgId;
        TechnicianId = technicianId;
        Entity = entity;
        EntityId = entityId;
        Type = type;
        Payload = payload;
        BaseVersion = baseVersion;
        ClientTs = clientTs;
        AppliedAt = appliedAt;
    }

    /// <summary>The device's own id for the operation, and the key the log dedupes by.</summary>
    public SyncOpId Id { get; private set; }

    /// <summary>The tenant whose data the operation changed.</summary>
    public OrgId OrgId { get; private set; }

    /// <summary>Who did it.</summary>
    public TechnicianId TechnicianId { get; private set; }

    /// <summary>What kind of thing it was done to — <c>"job"</c>, <c>"line_item"</c>.</summary>
    public string Entity { get; private set; }

    /// <summary>
    /// Which one.
    /// </summary>
    /// <remarks>
    /// A bare <see cref="Guid"/>, and the one identity here that is not typed: what it points at
    /// depends on <see cref="Entity"/>, so there is no single type it could be. Resolving it is
    /// the push handler's business, once it knows what kind of op it is holding.
    /// </remarks>
    public Guid EntityId { get; private set; }

    /// <summary>What was done — <c>"status_change"</c>, <c>"add_note"</c>.</summary>
    public string Type { get; private set; }

    /// <summary>
    /// The operation's own data, as JSON.
    /// </summary>
    /// <remarks>
    /// Kept verbatim rather than shredded into columns. Its shape depends on
    /// <see cref="Type"/> and the set of types grows with the technician app, so a schema that
    /// described them would need a migration every time the app learned to record something new.
    /// Stored as <c>jsonb</c>, so it is still queryable the day something needs to ask.
    /// </remarks>
    public string Payload { get; private set; }

    /// <summary>
    /// The version of the entity the device was looking at when it acted.
    /// </summary>
    /// <remarks>
    /// Recorded rather than merely checked: it is what a stale write was judged against, and
    /// without it the log cannot explain why an op was applied to the entity it was applied to.
    /// </remarks>
    public long BaseVersion { get; private set; }

    /// <summary>
    /// When it happened on the device — which is not when it arrived, and can be hours earlier.
    /// </summary>
    /// <remarks>
    /// The ordering a technician would recognise, and what last-write-wins on free text is
    /// decided by. It is a device's clock, so it is evidence rather than truth: two phones
    /// disagreeing about the time is an ordinary thing that this system cannot fix.
    /// </remarks>
    public DateTimeOffset ClientTs { get; private set; }

    /// <summary>When the server accepted it.</summary>
    public DateTimeOffset AppliedAt { get; private set; }

    /// <summary>
    /// Records an operation the server has applied.
    /// </summary>
    /// <remarks>
    /// The instant is passed in rather than read from a clock, so the record belongs to the same
    /// moment as the change it accompanies — the two are written in one transaction, and a log
    /// entry timed independently of the work it describes would be a second opinion about when
    /// the push happened.
    /// </remarks>
    /// <param name="id">The device's id for the operation.</param>
    /// <param name="orgId">The tenant.</param>
    /// <param name="technicianId">Who performed it.</param>
    /// <param name="entity">What kind of thing it was done to.</param>
    /// <param name="entityId">Which one.</param>
    /// <param name="type">What was done.</param>
    /// <param name="payload">The operation's data, as JSON.</param>
    /// <param name="baseVersion">The version the device was working from.</param>
    /// <param name="clientTs">When the device says it happened.</param>
    /// <param name="appliedAt">When the server accepted it.</param>
    /// <exception cref="ArgumentException">
    /// The operation is not complete enough to be a record of anything: no id, or a missing
    /// entity, type or payload.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The base version is negative.</exception>
    public static SyncOpRecord Applied(
        SyncOpId id,
        OrgId orgId,
        TechnicianId technicianId,
        string entity,
        Guid entityId,
        string type,
        string payload,
        long baseVersion,
        DateTimeOffset clientTs,
        DateTimeOffset appliedAt)
    {
        // The id is the whole idempotency guarantee, so an empty one is not a degenerate case to
        // tolerate: every op sent with a default Guid would be the same op, and the second would
        // be skipped as a duplicate of unrelated work.
        if (id.Value == Guid.Empty)
        {
            throw new ArgumentException("A sync op needs the id its device gave it.", nameof(id));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);

        // Empty rather than absent: the column is jsonb, and an operation with nothing to say
        // still says it as JSON. What is inside is the push handler's business, not this type's.
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);

        ArgumentOutOfRangeException.ThrowIfNegative(baseVersion);

        return new SyncOpRecord(
            id,
            orgId,
            technicianId,
            entity,
            entityId,
            type,
            payload,
            baseVersion,
            clientTs,
            appliedAt);
    }
}
