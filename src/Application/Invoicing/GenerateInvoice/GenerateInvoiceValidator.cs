using FluentValidation;
using OpenDispatch.Application.Validation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Invoicing.GenerateInvoice;

/// <summary>
/// Shape rules for raising a bill.
/// </summary>
/// <remarks>
/// <para>
/// The bounds on quantity and price are the interesting ones. <c>Money</c> is held in cents and
/// its arithmetic is checked, so a mistyped amount does not produce a wrong total — it throws an
/// <c>OverflowException</c> from inside the domain, which is a 500 for a typing mistake. The limits
/// here sit far below what the type can hold and far above what a plumbing invoice looks like; they
/// exist to catch a slipped decimal point, not to express a business rule about prices.
/// </para>
/// <para>
/// An invoice with no lines is refused. The domain permits one — a total of nothing is arithmetic
/// rather than nonsense — but a bill for nothing is not something a shop sends, and it would move
/// the job to <c>Invoiced</c>, which is the transition that can only happen once.
/// </para>
/// </remarks>
internal sealed class GenerateInvoiceValidator : AbstractValidator<GenerateInvoiceCommand>
{
    /// <summary>The most of anything one line can bill for.</summary>
    internal const decimal MaxQuantity = 100_000m;

    /// <summary>The largest price a line can carry, either way — a discount is a negative price.</summary>
    internal const decimal MaxUnitPrice = 1_000_000m;

    public GenerateInvoiceValidator()
    {
        RuleFor(command => command.JobId)
            .NotEqual(default(JobId)).WithMessage("A job must be named.");

        RuleFor(command => command.Lines)
            .NotEmpty().WithMessage("An invoice must bill for something.");

        RuleForEach(command => command.Lines).ChildRules(line =>
        {
            line.RuleFor(item => item.Kind)
                .IsInEnum().WithMessage("That is not a kind of line an invoice can carry.");

            line.RuleFor(item => item.Description)
                .NotEmpty().WithMessage("A billed line must say what it is for.")
                .MaximumLength(TextLimits.Description);

            line.RuleFor(item => item.Quantity)
                .GreaterThan(0m).WithMessage("A billed line must be for a positive quantity.")
                .LessThanOrEqualTo(MaxQuantity);

            line.RuleFor(item => item.UnitPrice)
                .InclusiveBetween(-MaxUnitPrice, MaxUnitPrice);
        });
    }
}
