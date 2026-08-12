using FluentValidation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.RemoveServiceLocation;

/// <summary>Refuses identifiers that were never minted — see <c>GetCustomerValidator</c>.</summary>
internal sealed class RemoveServiceLocationValidator : AbstractValidator<RemoveServiceLocationCommand>
{
    public RemoveServiceLocationValidator()
    {
        RuleFor(command => command.CustomerId)
            .NotEqual(default(CustomerId)).WithMessage("A customer must be named.");

        RuleFor(command => command.LocationId)
            .NotEqual(default(ServiceLocationId)).WithMessage("A service location must be named.");
    }
}
