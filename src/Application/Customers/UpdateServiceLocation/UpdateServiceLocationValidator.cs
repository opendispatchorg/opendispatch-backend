using FluentValidation;
using OpenDispatch.Application.Validation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.UpdateServiceLocation;

/// <summary>Shape rules for correcting a service location — identical to adding one.</summary>
internal sealed class UpdateServiceLocationValidator : AbstractValidator<UpdateServiceLocationCommand>
{
    public UpdateServiceLocationValidator()
    {
        RuleFor(command => command.CustomerId)
            .NotEqual(default(CustomerId)).WithMessage("A customer must be named.");

        RuleFor(command => command.LocationId)
            .NotEqual(default(ServiceLocationId)).WithMessage("A service location must be named.");

        RuleFor(command => command.Label)
            .NotEmpty().WithMessage("A service location must have a label.")
            .MaximumLength(TextLimits.Label);

        RuleFor(command => command.Address)
            .NotEmpty().WithMessage("A service location must have an address.")
            .MaximumLength(TextLimits.Address);

        RuleFor(command => command.Latitude).IsLatitude();
        RuleFor(command => command.Longitude).IsLongitude();
    }
}
