using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;

namespace OpenDispatch.Application.Invoicing.GenerateInvoice;

/// <summary>
/// Bills a finished job.
/// </summary>
/// <param name="JobId">The work being billed. Must be completed.</param>
/// <param name="Lines">What to charge for — time on the job, and parts fitted.</param>
/// <remarks>
/// <para>
/// The lines are stated rather than derived, because nothing in the system knows what the work
/// actually took: a job carries an <em>estimated</em> duration and no parts at all, and billing the
/// estimate would invoice for the plan rather than the visit. When the technician app starts
/// recording labour and parts in the field (Documents 6–7), those become the source and this
/// command is what they arrive through.
/// </para>
/// <para>
/// It is a command a person sends, not something that happens on completion. Document 2 §12 names
/// automatic invoicing as the flagship example of reacting to <c>JobCompleted</c> with a new
/// handler, and leaving it out is what keeps that a demonstration rather than a claim: nothing here
/// changes when somebody writes it.
/// </para>
/// </remarks>
public sealed record GenerateInvoiceCommand(JobId JobId, IReadOnlyList<InvoiceLine> Lines)
    : ICommand<InvoiceSummary>;

/// <summary>
/// One line to bill.
/// </summary>
/// <param name="Kind">Labour or a part.</param>
/// <param name="Description">What it says on the invoice.</param>
/// <param name="Quantity">How many — hours for labour, units for parts. Fractions are ordinary.</param>
/// <param name="UnitPrice">The price of one, in whole currency units. Negative for a discount.</param>
/// <remarks>
/// The price is a <c>decimal</c> of currency rather than a <c>Money</c> because <c>Money</c> is
/// held in cents and refuses an amount it cannot hold by throwing — the same reason every other
/// command in this phase carries primitives where a value object would refuse. A discount is a
/// negative price and never a negative quantity, which the domain also insists on: with both
/// conventions available, the sign of a line where somebody used the wrong one is anybody's guess.
/// </remarks>
public sealed record InvoiceLine(LineItemKind Kind, string Description, decimal Quantity, decimal UnitPrice);
