using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Domain.Tests.Events;

/// <summary>
/// The catalog is the system's extension seam: invoicing, notifications, inventory and
/// commission all arrive later as handlers for these names, and the client contracts are
/// generated from shapes that have to stay in step with them.
/// </summary>
/// <remarks>
/// So the catalog is pinned here rather than merely written down. Adding an eleventh event is
/// a deliberate act — it should fail this test, and whoever adds it should then think about
/// whether the clients and the contract pipeline need to know. It did its job for
/// <c>AssignmentPlanned</c>: the tenth was added, this failed, and the question got asked.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class DomainEventCatalogTests
{
    private static readonly string[] Catalog =
    [
        nameof(AssignmentChanged),
        nameof(AssignmentPlanned),
        nameof(InvoicePaid),
        nameof(InvoiceRaised),
        nameof(JobCancelled),
        nameof(JobCompleted),
        nameof(JobDispatched),
        nameof(JobEnRoute),
        nameof(JobInProgress),
        nameof(JobScheduled),
        nameof(JobUnscheduled),
    ];

    [Fact]
    public void TheDomainDeclaresExactlyTheCatalogedEvents()
    {
        var declared = typeof(IDomainEvent).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(IDomainEvent)))
            .Select(type => type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Catalog, declared);
    }

    [Fact]
    public void TheAggregatesBetweenThemRaiseEveryCatalogedEvent()
    {
        var raised = RaiseEverythingTheDomainCan();

        var names = raised
            .Select(domainEvent => domainEvent.GetType().Name)
            .Distinct()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Catalog, names);
    }

    [Fact]
    public void EveryRaisedEventRecordsWhenItHappened()
    {
        // A record that forgets to stamp OccurredAt does not fail anywhere obvious — it
        // just reports that everything happened in the year 1, which only shows up much
        // later in an audit trail or an ordering bug.
        var raised = RaiseEverythingTheDomainCan();

        Assert.All(raised, domainEvent => Assert.NotEqual(default, domainEvent.OccurredAt));
    }

    /// <summary>
    /// Drives the aggregates through every action that announces something, so the whole
    /// catalog is exercised through real raise-sites rather than by constructing events.
    /// </summary>
    private static List<IDomainEvent> RaiseEverythingTheDomainCan()
    {
        var raised = new List<IDomainEvent>();

        var worked = JobBuilder.Any().Build();
        worked.Schedule();
        worked.Dispatch();
        worked.MarkEnRoute();
        worked.MarkInProgress();
        worked.MarkCompleted(DateTimeOffset.UtcNow);
        raised.AddRange(worked.DomainEvents);

        var calledOff = JobBuilder.Any().Build();
        calledOff.Cancel();
        raised.AddRange(calledOff.DomainEvents);

        // The one move that goes backwards: work the optimiser had placed and can no longer fit.
        var withdrawn = JobBuilder.Any().Build();
        withdrawn.Schedule();
        withdrawn.Unschedule();
        raised.AddRange(withdrawn.DomainEvents);

        var stop = AssignmentBuilder.Any().Build();
        stop.Reassign(TechnicianId.New());
        raised.AddRange(stop.DomainEvents);

        var invoice = InvoiceBuilder.Any().Build();
        invoice.MarkPaid();
        raised.AddRange(invoice.DomainEvents);

        return raised;
    }
}
