using FluentValidation;
using OpenDispatch.Application.Validation;

namespace OpenDispatch.Application.Customers.CreateCustomer;

/// <summary>
/// Shape rules for taking on a customer.
/// </summary>
/// <remarks>
/// Contact details are optional: Document 1's customer is a shop's record of somebody, and nothing
/// requires that somebody to be reachable. What is refused is a value that is present and
/// implausible, because a mistyped email is worse than a missing one — it looks like a way to
/// reach them. The lengths come from <see cref="TextLimits"/>, so a name is as long here as it is
/// anywhere else.
/// </remarks>
internal sealed class CreateCustomerValidator : AbstractValidator<CreateCustomerCommand>
{
    public CreateCustomerValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("A customer must have a name.")
            .MaximumLength(TextLimits.Name);

        // Blank is absent, not invalid: ContactInfo normalises an untouched form field to null,
        // so a rule that fired on one would refuse the ordinary case of leaving it empty.
        RuleFor(command => command.Email)
            .MaximumLength(TextLimits.Email)
            .EmailAddress().WithMessage("That does not look like an email address.")
            .When(command => !string.IsNullOrWhiteSpace(command.Email));

        RuleFor(command => command.Phone)
            .MaximumLength(TextLimits.Phone)
            .When(command => !string.IsNullOrWhiteSpace(command.Phone));
    }
}
