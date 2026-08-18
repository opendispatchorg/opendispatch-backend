using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Customers.UpdateServiceLocation;

/// <summary>
/// Loads the customer and corrects the location — checking it is theirs before asking the
/// aggregate, the same way <c>CreateJobHandler</c> does, so an unknown location is reported as a
/// miss rather than surfacing as the <c>DomainException</c> the aggregate would throw for a
/// caller that skipped the check.
/// </summary>
internal sealed class UpdateServiceLocationHandler(ICustomerRepository customers)
    : IRequestHandler<UpdateServiceLocationCommand, Result>
{
    public async Task<Result> Handle(UpdateServiceLocationCommand command, CancellationToken cancellationToken)
    {
        var customer = await customers.GetAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);

        if (customer is null)
        {
            return Result.Failure(CustomerErrors.NotFound(command.CustomerId));
        }

        if (customer.IsErased)
        {
            return Result.Failure(CustomerErrors.Erased(command.CustomerId));
        }

        if (customer.Locations.All(location => location.Id != command.LocationId))
        {
            return Result.Failure(CustomerErrors.LocationNotFound(command.CustomerId, command.LocationId));
        }

        customer.UpdateLocation(
            command.LocationId,
            command.Label,
            command.Address,
            new GeoPoint(command.Latitude, command.Longitude));

        return Result.Success();
    }
}
