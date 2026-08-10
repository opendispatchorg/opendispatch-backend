using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Api.IntegrationTests.Persistence;

/// <summary>
/// A throwaway aggregate that carries one of everything step 25 teaches EF to store.
/// </summary>
/// <remarks>
/// <para>
/// It exists so the conversions can be proved against a real database before any real aggregate
/// is mapped, and it is shaped like one deliberately: private setters, a typed id for a key,
/// value objects rather than primitives, and <see cref="AggregateRoot"/> for the version stamp.
/// A row of loose columns would have exercised the converters without exercising the way they
/// are actually reached.
/// </para>
/// <para>
/// Every strongly-typed id has a property here, which is the point of an otherwise absurd shape.
/// The converters cannot be wrong in a way that compiles — a <see cref="CustomerId"/> column
/// cannot quietly hold a <see cref="JobId"/> — so the only failure they have is one going
/// unregistered, and the only way to catch that is to have the model contain all eight.
/// </para>
/// <para>
/// The private parameterless constructor is the part step 26 needs to read. EF binds constructor
/// parameters only to <em>mapped properties</em>, and a complex property is not one — so
/// <c>ConverterProbe(… TimeWindow window)</c> alone leaves EF with no way to build the type at
/// all. With a parameterless constructor it materialises the way Document 2 §6 describes instead,
/// setting private setters and backing fields. Every aggregate holding a
/// <see cref="TimeWindow"/> — <c>Job</c> and <c>Technician</c> — has the same constructor shape
/// and will need the same thing.
/// </para>
/// </remarks>
internal sealed class ConverterProbe : AggregateRoot
{
    private ConverterProbe()
    {
    }

    private ConverterProbe(
        JobId id,
        OrgId orgId,
        AssignmentId assignmentId,
        CustomerId customerId,
        InvoiceId invoiceId,
        LineItemId lineItemId,
        ServiceLocationId serviceLocationId,
        TechnicianId technicianId,
        Money amount,
        GeoPoint location,
        TimeWindow window)
    {
        Id = id;
        OrgId = orgId;
        AssignmentId = assignmentId;
        CustomerId = customerId;
        InvoiceId = invoiceId;
        LineItemId = lineItemId;
        ServiceLocationId = serviceLocationId;
        TechnicianId = technicianId;
        Amount = amount;
        Location = location;
        Window = window;
    }

    public JobId Id { get; private set; }

    public OrgId OrgId { get; private set; }

    public AssignmentId AssignmentId { get; private set; }

    public CustomerId CustomerId { get; private set; }

    public InvoiceId InvoiceId { get; private set; }

    public LineItemId LineItemId { get; private set; }

    public ServiceLocationId ServiceLocationId { get; private set; }

    public TechnicianId TechnicianId { get; private set; }

    public Money Amount { get; private set; }

    public GeoPoint Location { get; private set; }

    public TimeWindow Window { get; private set; }

    public static ConverterProbe Create(Money amount, GeoPoint location, TimeWindow window) =>
        new(
            JobId.New(),
            OrgId.New(),
            AssignmentId.New(),
            CustomerId.New(),
            InvoiceId.New(),
            LineItemId.New(),
            ServiceLocationId.New(),
            TechnicianId.New(),
            amount,
            location,
            window);

    /// <summary>Changes something, so a save has a modification to stamp a new version onto.</summary>
    public void MoveTo(GeoPoint location) => Location = location;
}
