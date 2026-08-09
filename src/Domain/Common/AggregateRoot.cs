namespace OpenDispatch.Domain.Common;

/// <summary>
/// Base class for aggregate roots: the entities that own a consistency boundary and are
/// loaded, saved and locked as a unit.
/// </summary>
/// <remarks>
/// <para>
/// It carries exactly two things every root needs and nothing else — the events it has
/// raised but not yet published, and the version stamp optimistic concurrency is built on.
/// Notably absent is an <c>Id</c>: each root has its own strongly-typed identifier, and a
/// shared base id would mean giving that up.
/// </para>
/// <para>
/// Events accumulate here rather than being published as they happen, so nothing is
/// announced until the transaction that produced it commits. The dispatcher drains them
/// after a successful save (step 31).
/// </para>
/// </remarks>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>
    /// Events raised since the last <see cref="ClearDomainEvents"/>, oldest first. Order is
    /// meaningful: handlers see them in the sequence the domain produced them.
    /// </summary>
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    /// <summary>
    /// The optimistic-concurrency stamp. Two dispatchers dragging the same job at once is
    /// an ordinary Tuesday, so the second write is detected rather than silently winning.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>
    /// Records that something happened. Protected because only the aggregate itself knows
    /// what it did — an event raised from outside would be an assertion about the domain
    /// by code that is not the domain.
    /// </summary>
    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    /// <summary>
    /// Drops the pending events. Called by the dispatcher once they have been published, so
    /// a second save does not republish them.
    /// </summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <summary>
    /// Advances the concurrency stamp. The hook the persistence layer calls when it writes
    /// a modified root (step 25); no aggregate bumps its own version.
    /// </summary>
    public void BumpVersion() => Version++;
}
