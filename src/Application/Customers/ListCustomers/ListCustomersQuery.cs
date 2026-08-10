using OpenDispatch.Application.Messaging;

namespace OpenDispatch.Application.Customers.ListCustomers;

/// <summary>
/// Every customer on this tenant's books, by name.
/// </summary>
/// <remarks>
/// <para>
/// It takes no parameters, which is also why it is the one request in this slice with no
/// validator: there is nothing to be malformed. An empty <c>AbstractValidator</c> would be a rule
/// that says nothing, run on every call, that a reader has to open to discover says nothing.
/// </para>
/// <para>
/// It is also unpaged, following the port. That will not age well — a customer list grows with the
/// business rather than with the crew — and the answer when it stops fitting is a projection
/// through a read model with paging and a search term, not parameters bolted onto this.
/// </para>
/// </remarks>
public sealed record ListCustomersQuery : IQuery<IReadOnlyList<CustomerSummary>>;
