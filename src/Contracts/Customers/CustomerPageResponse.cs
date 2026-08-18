namespace OpenDispatch.Contracts.Customers;

/// <summary>
/// One page of customers. The body of <c>GET /customers?page={n}&amp;pageSize={n}</c>.
/// </summary>
/// <remarks>
/// <para>
/// A page rather than a bare array, which is a breaking change to this endpoint and a deliberate
/// one: the array had no end. A client that drew a picker from it was being handed every customer
/// the business had ever taken on, and the fix cannot be a header — a caller needs the total to
/// know there is a page after this one.
/// </para>
/// <para>
/// A concrete record per endpoint rather than one generic envelope, because the contract generator
/// emits records rather than generics and a client reading TypeScript should not have to
/// instantiate a type parameter to find out what a customer is.
/// </para>
/// </remarks>
/// <param name="Items">The customers on this page, by name.</param>
/// <param name="Total">How many the tenant has altogether.</param>
/// <param name="Page">Which page this is, counting from one.</param>
/// <param name="PageSize">How many this page was allowed to hold.</param>
public sealed record CustomerPageResponse(
    IReadOnlyList<CustomerSummaryResponse> Items,
    int Total,
    int Page,
    int PageSize);
