namespace OpenDispatch.Contracts.Customers;

/// <summary>One of a customer's service locations, as the clients see it.</summary>
/// <param name="Id">The identity a job points at.</param>
/// <param name="Label">What the customer calls it.</param>
/// <param name="Address">The postal address a technician would be given.</param>
/// <param name="Latitude">Where it is, in decimal degrees.</param>
/// <param name="Longitude">Where it is, in decimal degrees.</param>
public sealed record ServiceLocationResponse(Guid Id, string Label, string Address, double Latitude, double Longitude);
