using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Customers.RemoveServiceLocation;

/// <summary>Loads the customer and takes the location off their record.</summary>
internal sealed class RemoveServiceLocationHandler(ICustomerRepository customers)
    : IRequestHandler<RemoveServiceLocationCommand, Result>
{
    public async Task<Result> Handle(RemoveServiceLocationCommand command, CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);

        if (customer is null)
        {
            return Result.Failure(CustomerErrors.NotFound(command.CustomerId));
        }

        if (customer.Locations.All(location => location.Id != command.LocationId))
        {
            return Result.Failure(CustomerErrors.LocationNotFound(command.CustomerId, command.LocationId));
        }

        customer.RemoveLocation(command.LocationId);

        return Result.Success();
    }
}
