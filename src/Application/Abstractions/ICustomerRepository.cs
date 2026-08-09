using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Loads and stores customers, and with them the service locations they own.
/// </summary>
/// <remarks>
/// There is no port for a service location, and that is the aggregate rule showing through:
/// a location is reached, added and removed only through its customer, so a repository over
/// one would be a way to hold something that has no independent existence. A job crossing
/// that boundary carries a <see cref="ServiceLocationId"/>, not a location.
/// </remarks>
public interface ICustomerRepository
{
    /// <summary>
    /// Fetches one customer, with their locations, for changing.
    /// </summary>
    /// <returns>The customer, or <see langword="null"/> if this tenant has no such customer.</returns>
    Task<Customer?> GetAsync(CustomerId id, CancellationToken ct);

    /// <summary>Stages a newly taken-on customer.</summary>
    void Add(Customer customer);

    /// <summary>
    /// Fetches every customer in the tenant.
    /// </summary>
    /// <remarks>
    /// The one query here that will not age well. A customer list is a read path — it feeds
    /// a picker, and it grows with the business rather than with the crew — so the moment it
    /// wants paging, sorting or a search term it should become a projection through a read
    /// model instead of gaining parameters here.
    /// </remarks>
    Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct);
}
