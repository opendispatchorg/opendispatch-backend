using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Customers.AddServiceLocation;

/// <summary>
/// Loads the customer and asks them to add the location.
/// </summary>
/// <remarks>
/// A location is created through its customer and nowhere else, which is the aggregate boundary
/// doing its job: there is no service-location repository to reach for, so there is no way to
/// write one that belongs to nobody. The customer is loaded to be changed, so it is loaded whole
/// — the owned locations come with it, and the pipeline saves what changed.
/// </remarks>
internal sealed class AddServiceLocationHandler(ICustomerRepository customers)
    : IRequestHandler<AddServiceLocationCommand, Result<ServiceLocationId>>
{
    public async Task<Result<ServiceLocationId>> Handle(
        AddServiceLocationCommand command,
        CancellationToken cancellationToken)
    {
        var customer = await customers
            .GetAsync(command.CustomerId, cancellationToken)
            .ConfigureAwait(false);

        if (customer is null)
        {
            return Result.Failure<ServiceLocationId>(CustomerErrors.NotFound(command.CustomerId));
        }

        if (customer.IsErased)
        {
            return Result.Failure<ServiceLocationId>(CustomerErrors.Erased(command.CustomerId));
        }

        var location = customer.AddLocation(
            command.Label,
            command.Address,
            new GeoPoint(command.Latitude, command.Longitude));

        return Result.Success(location);
    }
}
