using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Customers.ListCustomers;

/// <summary>
/// Reads the tenant's customers and summarises them.
/// </summary>
/// <remarks>
/// The order is the repository's, by name, and is not restated here — two places deciding how a
/// list is sorted is one place too many, and the one that knows is the one that can ask the
/// database to do it.
/// </remarks>
internal sealed class ListCustomersHandler(ICustomerRepository customers)
    : IRequestHandler<ListCustomersQuery, Result<IReadOnlyList<CustomerSummary>>>
{
    public async Task<Result<IReadOnlyList<CustomerSummary>>> Handle(
        ListCustomersQuery query,
        CancellationToken cancellationToken)
    {
        var found = await customers.ListAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<CustomerSummary> summaries =
        [
            .. found.Select(customer => new CustomerSummary(
                customer.Id,
                customer.Name,
                customer.Contact.Email,
                customer.Contact.Phone)),
        ];

        return Result.Success(summaries);
    }
}
