using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.GetCustomer;

/// <summary>
/// One customer as a reader sees them: who they are, how to reach them, and where they want work
/// done.
/// </summary>
/// <param name="Id">Their identity.</param>
/// <param name="Name">Their name, personal or trading.</param>
/// <param name="Email">Their email address, or <see langword="null"/> if there isn't one.</param>
/// <param name="Phone">Their phone number, or <see langword="null"/> if there isn't one.</param>
/// <param name="Locations">The places they want work done, in the order they were added.</param>
/// <param name="ErasedAt">
/// When they asked to be forgotten, or <see langword="null"/> if they did not — which is what says
/// that the tombstones above are an answered request rather than a record somebody has mangled.
/// </param>
/// <remarks>
/// <para>
/// A projection rather than the <c>Customer</c> itself. Handing an aggregate outwards would give
/// a caller the intent methods that enforce its rules with no unit of work to save what they
/// changed — an object that looks mutable and silently is not. It would also carry the
/// organization the row belongs to, which is nothing a caller needs to be told: whose data this
/// is was decided before the query ran.
/// </para>
/// <para>
/// The identifiers stay strongly typed. This is an internal shape, not a wire shape — step 47's
/// DTOs are the external ones, mapped at the edge and nowhere deeper.
/// </para>
/// </remarks>
/// <param name="RetiredAt">When they were taken off the books, or <see langword="null"/> while current.</param>
public sealed record CustomerDetail(
    CustomerId Id,
    string Name,
    string? Email,
    string? Phone,
    IReadOnlyList<ServiceLocationDetail> Locations,
    DateTimeOffset? ErasedAt,
    DateTimeOffset? RetiredAt);

/// <summary>
/// One of a customer's service locations.
/// </summary>
/// <param name="Id">The identity a job points at.</param>
/// <param name="Label">What the customer calls it.</param>
/// <param name="Address">The postal address a technician would be given.</param>
/// <param name="Latitude">Where it is, in decimal degrees.</param>
/// <param name="Longitude">Where it is, in decimal degrees.</param>
/// <remarks>
/// The coordinate is flattened to two numbers for the same reason the command carries two: this
/// shape and the shape that creates it should read the same way round.
/// </remarks>
public sealed record ServiceLocationDetail(
    ServiceLocationId Id,
    string Label,
    string Address,
    double Latitude,
    double Longitude);
