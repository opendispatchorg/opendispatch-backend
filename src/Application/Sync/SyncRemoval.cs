using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Sync;

/// <summary>
/// A note that something left a technician's day, kept because the thing itself is gone.
/// </summary>
/// <remarks>
/// <para>
/// Pull answers "what changed since?" by reading rows and their change stamps, which works for
/// everything except a row that no longer exists. Re-optimising a day deletes the stops it could
/// not keep (step 37), and a deleted stop cannot carry a stamp saying so — so a phone that is never
/// told keeps it and drives to it. Document 2 §10 names that failure and step 21 gave the wire a
/// way to say it; this is what the server reads to say it.
/// </para>
/// <para>
/// It is written where the deletion happens — <c>AssignmentRepository.Remove</c> — rather than
/// inferred later, because after the transaction commits there is nothing left to infer from. The
/// tombstone and the deletion are one save, so a stop cannot vanish without the note that says it
/// did.
/// </para>
/// <para>
/// The other way a stop leaves a day needs none of this: handing it to another technician changes
/// the row rather than deleting it, so the row's own stamp reports it. Two ways in, one answer out.
/// </para>
/// <para>
/// <c>SyncLogPruner</c> deletes these past the retention window, alongside the sync op log — the
/// two tables nothing else ever deletes from. The window is how far behind a device may be and
/// still be told about a deletion individually; one further behind learns the whole truth on a full
/// resync instead. A deployment schedules it (<c>prune --days 30</c>); without that they grow
/// forever.
/// </para>
/// </remarks>
public sealed class SyncRemoval
{
    // Materialisation constructor — see the note on Job.
    private SyncRemoval() => Entity = string.Empty;

    private SyncRemoval(
        Guid entityId,
        string entity,
        OrgId orgId,
        TechnicianId technicianId,
        DateTimeOffset removedAt)
    {
        EntityId = entityId;
        Entity = entity;
        OrgId = orgId;
        TechnicianId = technicianId;
        RemovedAt = removedAt;
    }

    /// <summary>
    /// What is gone, by the id the device knows it as. The key, because a thing is removed once.
    /// </summary>
    public Guid EntityId { get; private set; }

    /// <summary>What kind of thing it was — <c>"assignment"</c>, the only kind removed today.</summary>
    public string Entity { get; private set; }

    /// <summary>The tenant it belonged to.</summary>
    public OrgId OrgId { get; private set; }

    /// <summary>Whose day it left. A removal is only news to the technician who was going to it.</summary>
    public TechnicianId TechnicianId { get; private set; }

    /// <summary>When it was removed.</summary>
    /// <remarks>
    /// Not what pull windows by — that is the row's change stamp, like everything else — but what
    /// anybody clearing these out later will need.
    /// </remarks>
    public DateTimeOffset RemovedAt { get; private set; }

    /// <summary>Records that a planned stop is no longer on a technician's day.</summary>
    /// <param name="assignmentId">The stop that is gone.</param>
    /// <param name="orgId">The tenant.</param>
    /// <param name="technicianId">Whose day it left.</param>
    /// <param name="removedAt">When.</param>
    public static SyncRemoval OfStop(
        AssignmentId assignmentId,
        OrgId orgId,
        TechnicianId technicianId,
        DateTimeOffset removedAt) =>
        new(assignmentId.Value, StopEntity, orgId, technicianId, removedAt);

    /// <summary>The entity name a removed stop is reported under, matching the operation vocabulary.</summary>
    public const string StopEntity = "assignment";
}
