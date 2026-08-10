using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Domain.Customers;

/// <summary>
/// A customer and the places they want work done.
/// </summary>
/// <remarks>
/// The locations are owned, not referenced: they are created, read and removed only through
/// the customer, and there is no way to hold one without holding the customer it belongs to.
/// A job crossing that boundary takes the location's id, not the location.
/// </remarks>
public sealed class Customer : AggregateRoot
{
    private readonly List<ServiceLocation> _locations = [];

    // Materialisation constructor — see the note on Job.
    private Customer() => Name = string.Empty;

    private Customer(CustomerId id, OrgId orgId, string name, ContactInfo contact)
    {
        Id = id;
        OrgId = orgId;
        Name = name;
        Contact = contact;
    }

    /// <summary>This customer's identity.</summary>
    public CustomerId Id { get; private set; }

    /// <summary>The tenant whose books they are on.</summary>
    public OrgId OrgId { get; private set; }

    /// <summary>Their name, personal or trading.</summary>
    public string Name { get; private set; }

    /// <summary>How to reach them.</summary>
    public ContactInfo Contact { get; private set; }

    /// <summary>The places they want work done, in the order they were added.</summary>
    public IReadOnlyList<ServiceLocation> Locations => _locations.AsReadOnly();

    /// <summary>Takes on a new customer, with no locations yet.</summary>
    /// <exception cref="DomainException">The customer has no name.</exception>
    public static Customer Create(OrgId orgId, string name, ContactInfo contact)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("A customer must have a name.");
        }

        return new Customer(CustomerId.New(), orgId, name.Trim(), contact);
    }

    /// <summary>
    /// Adds a place they want work done, and hands back its identity — which is what a job
    /// needs in order to point at it.
    /// </summary>
    /// <exception cref="DomainException">The location could not be found or driven to.</exception>
    public ServiceLocationId AddLocation(string label, string address, GeoPoint point)
    {
        var location = ServiceLocation.Create(label, address, point);
        _locations.Add(location);

        return location.Id;
    }

    /// <summary>
    /// Takes a location off their record.
    /// </summary>
    /// <exception cref="DomainException">
    /// They have no such location. Unlike a technician's skills, this refuses rather than
    /// shrugging: removing a location the caller believes exists means the caller is working
    /// from a different picture of the world, and silently succeeding would hide that.
    /// </exception>
    public void RemoveLocation(ServiceLocationId locationId)
    {
        if (_locations.RemoveAll(location => location.Id == locationId) == 0)
        {
            throw new DomainException($"This customer has no service location {locationId.Value}.");
        }
    }
}
