using FluentValidation;
using OpenDispatch.Application.Validation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.UpdateCustomer;

/// <summary>Shape rules for correcting a customer, identical to taking one on — see <c>CreateCustomerValidator</c>.</summary>
internal sealed class UpdateCustomerValidator : AbstractValidator<UpdateCustomerCommand>
{
    public UpdateCustomerValidator()
    {
        RuleFor(command => command.Id)
            .NotEqual(default(CustomerId)).WithMessage("A customer must be named.");

        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("A customer must have a name.")
            .MaximumLength(TextLimits.Name);

        RuleFor(command => command.Email)
            .MaximumLength(TextLimits.Email)
            .EmailAddress().WithMessage("That does not look like an email address.")
            .When(command => !string.IsNullOrWhiteSpace(command.Email));

        RuleFor(command => command.Phone)
            .MaximumLength(TextLimits.Phone)
            .When(command => !string.IsNullOrWhiteSpace(command.Phone));
    }
}
