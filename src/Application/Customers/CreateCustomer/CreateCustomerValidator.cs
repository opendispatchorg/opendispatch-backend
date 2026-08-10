using FluentValidation;

namespace OpenDispatch.Application.Customers.CreateCustomer;

/// <summary>
/// Shape rules for taking on a customer.
/// </summary>
/// <remarks>
/// <para>
/// The lengths are here rather than on the columns, and that is deliberate: the schema stores
/// these as Postgres <c>text</c>, which costs nothing over <c>varchar(n)</c>, so a limit in the
/// database buys only a truncation error arriving from three layers away. A limit here is
/// reported to whoever typed it, keyed by the field it concerns.
/// </para>
/// <para>
/// The numbers themselves are judgment, not fact — long enough that no real trading name, address
/// or number is refused, short enough that a paste of an entire document is. They are the first
/// such rules in the system and every later slice should match them rather than invent its own.
/// </para>
/// <para>
/// Contact details are optional: Document 1's customer is a shop's record of somebody, and
/// nothing requires that somebody to be reachable. What is refused is a value that is present and
/// implausible, because a mistyped email is worse than a missing one — it looks like a way to
/// reach them.
/// </para>
/// </remarks>
internal sealed class CreateCustomerValidator : AbstractValidator<CreateCustomerCommand>
{
    /// <summary>Longest accepted customer name.</summary>
    internal const int MaxNameLength = 200;

    /// <summary>Longest accepted email address — the limit RFC 5321 puts on a path.</summary>
    internal const int MaxEmailLength = 320;

    /// <summary>Longest accepted phone number, with room for an international prefix and extension.</summary>
    internal const int MaxPhoneLength = 40;

    public CreateCustomerValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("A customer must have a name.")
            .MaximumLength(MaxNameLength);

        // Blank is absent, not invalid: ContactInfo normalises an untouched form field to null,
        // so a rule that fired on one would refuse the ordinary case of leaving it empty.
        RuleFor(command => command.Email)
            .MaximumLength(MaxEmailLength)
            .EmailAddress().WithMessage("That does not look like an email address.")
            .When(command => !string.IsNullOrWhiteSpace(command.Email));

        RuleFor(command => command.Phone)
            .MaximumLength(MaxPhoneLength)
            .When(command => !string.IsNullOrWhiteSpace(command.Phone));
    }
}
