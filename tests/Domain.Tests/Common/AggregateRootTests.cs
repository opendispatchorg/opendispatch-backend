using OpenDispatch.Domain.Common;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Domain.Tests.Common;

/// <summary>
/// The event buffer is the machinery every later feature hangs off — if events go missing
/// or survive a publish, invoicing and notifications silently misfire. The aggregates that
/// use it arrive in step 7 onward, so this exercises it through a stand-in.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class AggregateRootTests
{
    [Fact]
    public void NewAggregateHasNoPendingEvents()
    {
        var aggregate = new StubAggregate();

        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void RaisedEventsAccumulateInTheOrderTheyHappened()
    {
        var aggregate = new StubAggregate();
        var first = new StubEvent();
        var second = new StubEvent();

        aggregate.RaisePublicly(first);
        aggregate.RaisePublicly(second);

        Assert.Equal([first, second], aggregate.DomainEvents);
    }

    [Fact]
    public void ClearingEmptiesThePendingEvents()
    {
        var aggregate = new StubAggregate();
        aggregate.RaisePublicly(new StubEvent());

        aggregate.ClearDomainEvents();

        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void ClearingDoesNotStopTheAggregateRaisingAgain()
    {
        var aggregate = new StubAggregate();
        aggregate.RaisePublicly(new StubEvent());
        aggregate.ClearDomainEvents();
        var afterPublish = new StubEvent();

        aggregate.RaisePublicly(afterPublish);

        Assert.Equal([afterPublish], aggregate.DomainEvents);
    }

    [Fact]
    public void VersionStartsAtZeroAndAdvancesOnEachBump()
    {
        var aggregate = new StubAggregate();

        Assert.Equal(0, aggregate.Version);

        aggregate.BumpVersion();
        aggregate.BumpVersion();

        Assert.Equal(2, aggregate.Version);
    }

    /// <summary>
    /// A class, not a record: two records raised in the same tick would compare equal and
    /// quietly hide an ordering or duplication bug.
    /// </summary>
    private sealed class StubEvent : IDomainEvent
    {
        public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
    }

    /// <summary>Opens up <c>Raise</c>, which is protected so only real aggregates can call it.</summary>
    private sealed class StubAggregate : AggregateRoot
    {
        public void RaisePublicly(IDomainEvent domainEvent) => Raise(domainEvent);
    }
}
