using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Infrastructure.Events;

/// <summary>
/// A domain event written down in the same transaction as the work that raised it, so that the
/// reaction to it cannot be lost.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The gap this closes.</strong> Events used to be held in memory between the save and the
/// commit and published in-process afterwards. A subscriber that threw, or a process that died in
/// the moment between committing a job's cancellation and telling anything about it, lost the
/// reaction permanently — and lost it silently, which is what makes it worth a table: the stop for a
/// cancelled job stayed on a technician's phone and nobody found out until they drove there.
/// </para>
/// <para>
/// <strong>The row is written by the same <c>SaveChanges</c> as the aggregate.</strong> That is the
/// whole mechanism: either the job is cancelled and the fact is recorded, or neither happened. No
/// window, no reconciliation, nothing to sweep — the two are one write.
/// </para>
/// <para>
/// <strong>Delivered at least once.</strong> The publish that follows a commit deletes the row on
/// success; anything still here after the grace period is picked up again by
/// <see cref="OutboxDispatcher"/>. A process that dies between publishing and deleting therefore
/// publishes twice, which is why every subscriber in this system is written to be safe to run again
/// — withdrawing a stop that is already gone does nothing, and re-pushing a board event repaints a
/// board with what it already shows.
/// </para>
/// <para>
/// It is not a domain type and not an aggregate: it is the delivery mechanism's own bookkeeping,
/// with no invariant beyond being complete — the same reasoning that keeps <c>SyncOpRecord</c> out
/// of the domain.
/// </para>
/// </remarks>
public sealed class OutboxMessage
{
    // Materialisation constructor — see the note on Job.
    private OutboxMessage()
    {
        Type = string.Empty;
        Payload = string.Empty;
    }

    private OutboxMessage(Guid id, OrgId orgId, string type, string payload, DateTimeOffset occurredAt)
    {
        Id = id;
        OrgId = orgId;
        Type = type;
        Payload = payload;
        OccurredAt = occurredAt;
    }

    /// <summary>This message's identity, and what a claim locks.</summary>
    public Guid Id { get; private set; }

    /// <summary>
    /// Whose reaction this is — the organization owning the aggregate that raised the event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Without this the sweep could not work at all.</strong> Every subscriber it publishes
    /// to is tenant-scoped: the board notifier reads <c>ITenantContext.OrgId</c>, and the stop
    /// withdrawal reads assignments through a query filter built on it. A background dispatcher has
    /// resolved nobody, so reading it threw, the message was marked failed, and the row stayed —
    /// forever. The outbox was a table that filled up rather than a safety net.
    /// </para>
    /// <para>
    /// <strong>Nullable, for two reasons.</strong> Rows written before this column existed have no
    /// owner and cannot be given one honestly — they are skipped and named in the log rather than
    /// guessed at or deleted by a migration. And a nullable <c>OrgId</c> is deliberately not matched
    /// by <c>TenantOwnership.PropertyOf</c>, so no global query filter is applied to this table: the
    /// sweep has to read across tenants to find whose work is waiting. See
    /// <c>TenantQueryFilters</c>.
    /// </para>
    /// </remarks>
    public OrgId? OrgId { get; private set; }

    /// <summary>
    /// The event's CLR type, by full name, resolved back within the Domain assembly.
    /// </summary>
    /// <remarks>
    /// A name rather than an assembly-qualified string, because the assembly is known and a
    /// qualified name would bake a version into a row. Renaming or moving an event type is
    /// therefore a deployment concern: drain the outbox first, or the rows naming the old type
    /// cannot be delivered. That is a real constraint and is why it is written here rather than
    /// discovered.
    /// </remarks>
    public string Type { get; private set; }

    /// <summary>The event itself, as JSON.</summary>
    public string Payload { get; private set; }

    /// <summary>When the thing being described happened, as the event itself reports it.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>How many times delivery has been attempted and failed.</summary>
    /// <remarks>
    /// Kept rather than deleted-on-failure so a message nobody can deliver is visible instead of
    /// being retried forever in silence: the dispatcher logs and counts it, and the row is the
    /// evidence of what could not be delivered.
    /// </remarks>
    public int Attempts { get; private set; }

    /// <summary>What went wrong last time, if anything has.</summary>
    public string? LastError { get; private set; }

    /// <summary>When delivery was last attempted, or <see langword="null"/> if it never has been.</summary>
    public DateTimeOffset? LastAttemptAt { get; private set; }

    /// <summary>Records an event to be delivered.</summary>
    /// <param name="domainEvent">What happened.</param>
    /// <param name="payload">The event serialized.</param>
    /// <param name="orgId">
    /// The organization owning the aggregate that raised it — taken from the aggregate rather than
    /// from the ambient tenant, so a save made outside a request (the seeder, a CLI verb) records
    /// the right owner instead of throwing.
    /// </param>
    public static OutboxMessage For(IDomainEvent domainEvent, string payload, OrgId orgId)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        return new OutboxMessage(
            Guid.NewGuid(),
            orgId,
            domainEvent.GetType().FullName!,
            payload,
            domainEvent.OccurredAt);
    }

    /// <summary>Records that an attempt failed.</summary>
    /// <param name="error">What went wrong, trimmed to something a column can hold.</param>
    /// <param name="at">When the attempt was made.</param>
    public void Failed(string error, DateTimeOffset at)
    {
        Attempts++;
        LastError = error.Length <= MaximumErrorLength ? error : error[..MaximumErrorLength];
        LastAttemptAt = at;
    }

    /// <summary>How much of a failure is worth keeping: enough to recognise it, not a whole dump.</summary>
    private const int MaximumErrorLength = 2000;
}
