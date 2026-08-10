using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.AddServiceLocation;

/// <summary>
/// Adds a place the customer wants work done, and hands back its identity — which is what a job
/// needs in order to point at it.
/// </summary>
/// <param name="CustomerId">Whose record to add it to.</param>
/// <param name="Label">What the customer calls it — "Home", "Unit 4", "the Croydon branch".</param>
/// <param name="Address">The postal address a technician would be given.</param>
/// <param name="Latitude">Where it is, in decimal degrees between -90 and 90.</param>
/// <param name="Longitude">Where it is, in decimal degrees between -180 and 180.</param>
/// <remarks>
/// <para>
/// The coordinate arrives as two doubles rather than as a <c>GeoPoint</c>, and the reason is which
/// layer gets to refuse it. A <c>GeoPoint</c> cannot be constructed out of range — it throws — so
/// a command carrying one would be built at the edge, before any validator runs, and a longitude
/// of 4,000 would arrive as an unhandled exception rather than as a rejected field. Held as
/// doubles, the same value is a validation failure that names <c>Longitude</c>, and the value
/// object is constructed in the handler once the numbers are known to be a place.
/// </para>
/// <para>
/// The identifier is strongly typed for the opposite reason: a <c>CustomerId</c> cannot be
/// invalid, only unknown, and unknown is an answer the handler gives rather than a rejection the
/// validator makes.
/// </para>
/// </remarks>
public sealed record AddServiceLocationCommand(
    CustomerId CustomerId,
    string Label,
    string Address,
    double Latitude,
    double Longitude) : ICommand<ServiceLocationId>;
