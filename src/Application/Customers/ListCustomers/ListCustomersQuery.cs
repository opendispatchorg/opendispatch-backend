using OpenDispatch.Application.Messaging;

namespace OpenDispatch.Application.Customers.ListCustomers;

/// <summary>
/// One page of this tenant's customers, by name.
/// </summary>
/// <param name="Page">Which page, counting from one.</param>
/// <param name="PageSize">How many customers it may hold, up to <see cref="MaxPageSize"/>.</param>
/// <remarks>
/// <para>
/// Paged now, where it used to hand back every customer the business had ever taken on. The next
/// thing this will want is a search term, and the answer then is still what the port's own note
/// says: a projection through a read model, not a third parameter here.
/// </para>
/// <para>
/// The defaults matter more than the parameters: a caller that names neither gets a first page of a
/// sensible size, so an old client that has never heard of paging keeps working and simply stops
/// receiving the whole table.
/// </para>
/// </remarks>
public sealed record ListCustomersQuery(int Page = 1, int PageSize = ListCustomersQuery.DefaultPageSize)
    : IQuery<CustomerPage>
{
    /// <summary>What a caller gets when it does not ask.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>
    /// The largest page this endpoint will serve.
    /// </summary>
    /// <remarks>
    /// A cap rather than a suggestion: without one, <c>?pageSize=1000000</c> is the unpaged query
    /// again under a new name, and the caller who sends it will be a client author who found it
    /// easier than looping.
    /// </remarks>
    public const int MaxPageSize = 200;
}

/// <summary>
/// One page of customers, and how many there are to page through.
/// </summary>
/// <param name="Items">The customers on this page.</param>
/// <param name="Total">How many the tenant has altogether.</param>
/// <param name="Page">Which page this is.</param>
/// <param name="PageSize">How many it was allowed to hold.</param>
public sealed record CustomerPage(
    IReadOnlyList<CustomerSummary> Items,
    int Total,
    int Page,
    int PageSize);
