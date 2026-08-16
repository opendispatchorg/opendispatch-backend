using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Customers;

namespace OpenDispatch.Application.Customers.GetCustomer;

/// <summary>
/// Loads the customer and projects them.
/// </summary>
/// <remarks>
/// A query gets no transaction — the pipeline gives one only to commands — so nothing here can
/// change anything, whatever it does to the object it was handed. Projecting immediately is what
/// keeps that true beyond this method.
/// </remarks>
internal sealed class GetCustomerHandler(ICustomerRepository customers)
    : IRequestHandler<GetCustomerQuery, Result<CustomerDetail>>
{
    public async Task<Result<CustomerDetail>> Handle(
        GetCustomerQuery query,
        CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(query.Id, cancellationToken).ConfigureAwait(false);

        return customer is null
            ? Result.Failure<CustomerDetail>(CustomerErrors.NotFound(query.Id))
            : Result.Success(Project(customer));
    }

    private static CustomerDetail Project(Customer customer) => new(
        customer.Id,
        customer.Name,
        customer.Contact.Email,
        customer.Contact.Phone,
        [.. customer.Locations.Select(location => new ServiceLocationDetail(
            location.Id,
            location.Label,
            location.Address,
            location.Point.Lat,
            location.Point.Lng))],
        customer.ErasedAt);
}
