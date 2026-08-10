using FluentValidation;
using OpenDispatch.Application.Validation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.AddServiceLocation;

/// <summary>
/// Shape rules for a new service location.
/// </summary>
/// <remarks>
/// The coordinate rules are the ones that earn their place, and they are shared with every other
/// command that names a place — see <see cref="CoordinateRules"/> for why a command carries two
/// doubles rather than a <c>GeoPoint</c>.
/// </remarks>
internal sealed class AddServiceLocationValidator : AbstractValidator<AddServiceLocationCommand>
{
    public AddServiceLocationValidator()
    {
        RuleFor(command => command.CustomerId)
            .NotEqual(default(CustomerId)).WithMessage("A customer must be named.");

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
