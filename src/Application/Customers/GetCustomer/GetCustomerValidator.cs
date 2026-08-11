using FluentValidation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Customers.GetCustomer;

/// <summary>
/// Refuses an identifier that was never minted.
/// </summary>
/// <remarks>
/// An all-zeros <c>CustomerId</c> is the shape of an uninitialised field or an unparsed route
/// value, not of a customer that has been deleted — so it is a malformed request rather than a
/// miss, and saying so is more use to the caller than "no such customer" would be. Every other
/// identifier is simply looked up; unknown is the handler's answer, not the validator's.
/// </remarks>
internal sealed class GetCustomerValidator : AbstractValidator<GetCustomerQuery>
{
    public GetCustomerValidator() =>
        RuleFor(query => query.Id)
            .NotEqual(default(CustomerId)).WithMessage("A customer must be named.");
}
