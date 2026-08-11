using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.GetCustomer;

/// <summary>
/// Fetches one customer and their service locations.
/// </summary>
/// <param name="Id">Which customer.</param>
/// <remarks>
/// It reads through <c>ICustomerRepository</c> rather than a read model, because a customer with
/// their locations <em>is</em> the aggregate — there is no join to avoid and no wider set to page
/// through. The dispatch board is the path that earns a projection port (step 20), and the
/// difference between the two is the point of proportional abstraction.
/// </remarks>
public sealed record GetCustomerQuery(CustomerId Id) : IQuery<CustomerDetail>;
