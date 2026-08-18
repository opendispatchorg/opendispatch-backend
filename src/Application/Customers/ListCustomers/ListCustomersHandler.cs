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
    : IRequestHandler<ListCustomersQuery, Result<CustomerPage>>
{
    public async Task<Result<CustomerPage>> Handle(
        ListCustomersQuery query,
        CancellationToken cancellationToken)
    {
        var page = await customers
            .ListAsync(new PageRequest(query.Page, query.PageSize), cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(new CustomerPage(
            [
                .. page.Items.Select(customer => new CustomerSummary(
                    customer.Id,
                    customer.Name,
                    customer.Contact.Email,
                    customer.Contact.Phone)),
            ],
            page.Total,
            query.Page,
            query.PageSize));
    }
}
