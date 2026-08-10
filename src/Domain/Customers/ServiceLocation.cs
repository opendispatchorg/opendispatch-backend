using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Domain.Customers;

/// <summary>
/// A physical place a customer wants work done — a house, a site, one of several branches.
/// </summary>
/// <remarks>
/// Owned by <see cref="Customer"/> rather than being an aggregate of its own: a location has
/// no life apart from the customer it belongs to, and it is only ever loaded and saved with
/// them. It carries an identity because a <c>Job</c> points at one from outside that
/// boundary.
/// </remarks>
public sealed class ServiceLocation
{
    // Materialisation constructor — see the note on Job.
    private ServiceLocation()
    {
        Label = string.Empty;
        Address = string.Empty;
    }

    private ServiceLocation(ServiceLocationId id, string label, string address, GeoPoint point)
    {
        Id = id;
        Label = label;
        Address = address;
        Point = point;
    }

    /// <summary>This location's identity, which jobs reference.</summary>
    public ServiceLocationId Id { get; private set; }

    /// <summary>What the customer calls it — "Home", "Unit 4", "the Croydon branch".</summary>
    public string Label { get; private set; }

    /// <summary>The postal address a technician would be given.</summary>
    public string Address { get; private set; }

    /// <summary>Where it is, for routing.</summary>
    public GeoPoint Point { get; private set; }

    /// <summary>
    /// Creates a location. Internal because only <see cref="Customer"/> may mint one — a
    /// location that exists outside a customer is not a thing.
    /// </summary>
    /// <exception cref="DomainException">The location could not be found or driven to.</exception>
    internal static ServiceLocation Create(string label, string address, GeoPoint point)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new DomainException("A service location must have a label.");
        }

        if (string.IsNullOrWhiteSpace(address))
        {
            throw new DomainException("A service location must have an address.");
        }

        return new ServiceLocation(ServiceLocationId.New(), label.Trim(), address.Trim(), point);
    }
}
