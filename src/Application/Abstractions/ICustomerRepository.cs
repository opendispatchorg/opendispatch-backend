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
    /// Fetches one page of the tenant's customers, and how many there are altogether.
    /// </summary>
    /// <remarks>
    /// Paged since the day the unpaged version stopped being defensible: a customer list grows with
    /// the business rather than with the crew, and a shop with ten years of them was being handed
    /// all of them to draw a picker. A search term is the next thing this will want, and the note
    /// that used to be here still holds — at that point it becomes a projection through a read
    /// model rather than gaining another parameter.
    /// </remarks>
    Task<Page<Customer>> ListAsync(PageRequest page, CancellationToken ct);

    /// <summary>
    /// Every customer in the tenant, one at a time.
    /// </summary>
    /// <remarks>
    /// For the export, and only for the export: it is the one caller that genuinely wants all of
    /// them, and streaming is what keeps "all of them" from meaning "all of them in memory at
    /// once". Anything that wants to show a person a list wants <see cref="ListAsync"/>.
    /// </remarks>
    IAsyncEnumerable<Customer> StreamAsync(CancellationToken ct);
}
