using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;

namespace OpenDispatch.Application.Invoicing.GenerateInvoice;

/// <summary>
/// Bills a finished job.
/// </summary>
/// <param name="JobId">The work being billed. Must be completed.</param>
/// <param name="Lines">
/// What to charge for, when the caller is stating it. <see langword="null"/> — the ordinary case —
/// means bill what the technician recorded against the job.
/// </param>
/// <remarks>
/// <para>
/// <strong>Null and empty mean different things, and that is the whole point of the nullable.</strong>
/// Null is "bill the visit": the technician's own labour and parts, pushed up from the field as
/// <c>add_line_item</c> ops, become the invoice without the office re-typing them — which is
/// Document 1's "turn a completed job into an invoice from its labor and parts" rather than a
/// paraphrase of it. An empty list is a caller who supplied lines and supplied none, which is
/// refused by the validator as it always was.
/// </para>
/// <para>
/// Stated lines still win when they are given. A job whose field record is wrong, a warranty visit
/// billed differently, a call-out fee nobody stood in a house and typed — the office has the last
/// word, and taking it away would make the honest case easy and the ordinary correction impossible.
/// </para>
/// <para>
/// It is a command a person sends, not something that happens on completion. Document 2 §12 names
/// automatic invoicing as the flagship example of reacting to <c>JobCompleted</c> with a new
/// handler, and leaving it out is what keeps that a demonstration rather than a claim: nothing here
/// changes when somebody writes it — and now that the lines derive themselves, that handler is a
/// single <c>Send</c> with no line-building of its own.
/// </para>
/// </remarks>
public sealed record GenerateInvoiceCommand(JobId JobId, IReadOnlyList<InvoiceLine>? Lines = null)
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
