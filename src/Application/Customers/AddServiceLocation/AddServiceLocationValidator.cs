using FluentValidation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.AddServiceLocation;

/// <summary>
/// Shape rules for a new service location.
/// </summary>
/// <remarks>
/// <para>
/// The coordinate rules are the ones that earn their place. <c>GeoPoint</c> refuses anything that
/// is not a real place, and it refuses it by throwing — so without these the first sign of a
/// mistyped longitude is an exception from three layers in. The bounds are stated twice on
/// purpose: here so the caller is told, and there so the scheduler can treat every point it is
/// handed as somewhere a van can drive to.
/// </para>
/// <para>
/// <c>NaN</c> is not caught by the range rules — it compares false against every bound, so
/// <c>InclusiveBetween</c> refuses it, but only by accident of that comparison. The explicit
/// finiteness rule says so deliberately, and matches the check inside <c>GeoPoint</c>.
/// </para>
/// </remarks>
internal sealed class AddServiceLocationValidator : AbstractValidator<AddServiceLocationCommand>
{
    /// <summary>Longest accepted label.</summary>
    internal const int MaxLabelLength = 100;

    /// <summary>Longest accepted address, with room for several lines.</summary>
    internal const int MaxAddressLength = 500;

    public AddServiceLocationValidator()
    {
        RuleFor(command => command.CustomerId)
            .NotEqual(default(CustomerId)).WithMessage("A customer must be named.");

        RuleFor(command => command.Label)
            .NotEmpty().WithMessage("A service location must have a label.")
            .MaximumLength(MaxLabelLength);

        RuleFor(command => command.Address)
            .NotEmpty().WithMessage("A service location must have an address.")
            .MaximumLength(MaxAddressLength);

        // Stop at the first failure, so a NaN is reported as not being a number rather than also
        // as being outside a range it cannot meaningfully be compared to.
        RuleFor(command => command.Latitude)
            .Cascade(CascadeMode.Stop)
            .Must(double.IsFinite).WithMessage("A latitude must be a number.")
            .InclusiveBetween(-90d, 90d);

        RuleFor(command => command.Longitude)
            .Cascade(CascadeMode.Stop)
            .Must(double.IsFinite).WithMessage("A longitude must be a number.")
            .InclusiveBetween(-180d, 180d);
    }
}
