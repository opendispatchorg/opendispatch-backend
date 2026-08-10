using OpenDispatch.Domain.Common;

namespace OpenDispatch.Infrastructure.Events;

/// <summary>
/// The events raised during this request, waiting for the transaction that produced them to
/// commit.
/// </summary>
/// <remarks>
/// <para>
/// Scoped, so it is per request — the same lifetime as the <c>DbContext</c> whose interceptor
/// fills it and the transaction whose commit empties it.
/// </para>
/// <para>
/// It exists because collecting and publishing happen at two different moments. The events have
/// to be taken off the aggregates while the change tracker still has them, which is the save;
/// they must not be announced until the work is real, which is the commit. Between those two
/// they live here.
/// </para>
/// </remarks>
internal sealed class DomainEventQueue
{
    private readonly List<IDomainEvent> _pending = [];

    /// <summary>
    /// Takes custody of events an aggregate raised, in the order it raised them.
    /// </summary>
    /// <remarks>
    /// Copies rather than holds the caller's sequence: the caller clears the aggregate
    /// immediately afterwards, and <c>AggregateRoot.DomainEvents</c> is a view over the list
    /// being cleared rather than a snapshot of it.
    /// </remarks>
    public void Enqueue(IEnumerable<IDomainEvent> domainEvents) => _pending.AddRange(domainEvents);

    /// <summary>Hands over everything queued and empties the queue.</summary>
    public IReadOnlyList<IDomainEvent> Drain()
    {
        if (_pending.Count == 0)
        {
            return [];
        }

        var drained = _pending.ToArray();
        _pending.Clear();

        return drained;
    }

    /// <summary>
    /// Throws the queue away. What is in it describes work that was not committed, and a fact
    /// that did not happen must not be announced.
    /// </summary>
    public void Discard() => _pending.Clear();
}
