using FluentValidation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Invoicing.MarkPaid;

/// <summary>
/// Shape rules for settling a bill.
/// </summary>
/// <remarks>
/// One field, one rule. Whether the invoice exists, whether it is already settled and whether the
/// money can be taken all need the invoice and the gateway, and are the handler's answers as
/// conflicts.
/// </remarks>
internal sealed class MarkPaidValidator : AbstractValidator<MarkPaidCommand>
{
    public MarkPaidValidator() =>
        RuleFor(command => command.InvoiceId)
            .NotEqual(default(InvoiceId)).WithMessage("An invoice must be named.");
}
