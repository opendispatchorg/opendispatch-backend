using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.UpdateServiceLocation;

/// <summary>Corrects a place a customer wants work done — see <c>AddServiceLocationCommand</c> for the same shape.</summary>
/// <param name="CustomerId">Whose record it is on.</param>
/// <param name="LocationId">Which location.</param>
/// <param name="Label">What the customer calls it now.</param>
/// <param name="Address">The postal address a technician would be given.</param>
/// <param name="Latitude">Where it is, in decimal degrees between -90 and 90.</param>
/// <param name="Longitude">Where it is, in decimal degrees between -180 and 180.</param>
public sealed record UpdateServiceLocationCommand(
    CustomerId CustomerId,
    ServiceLocationId LocationId,
    string Label,
    string Address,
    double Latitude,
    double Longitude) : ICommand;
