namespace OpenDispatch.Contracts.Customers;

/// <summary>The body of <c>POST /customers/{id}/locations</c> and <c>PUT .../locations/{locationId}</c>.</summary>
/// <param name="Label">What the customer calls it — "Home", "Unit 4", "the Croydon branch".</param>
/// <param name="Address">The postal address a technician would be given.</param>
/// <param name="Latitude">Where it is, in decimal degrees between -90 and 90.</param>
/// <param name="Longitude">Where it is, in decimal degrees between -180 and 180.</param>
public sealed record ServiceLocationRequest(string Label, string Address, double Latitude, double Longitude);
