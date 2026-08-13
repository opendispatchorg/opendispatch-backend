using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Invoicing;

/// <summary>
/// An invoice as a reader sees it: what it bills for, and what it comes to.
/// </summary>
/// <param name="Id">Its identity.</param>
/// <param name="JobId">The job it bills.</param>
/// <param name="Status">Whether it has been settled.</param>
/// <param name="Issued">When it was raised.</param>
/// <param name="Lines">What it bills for, in the order the lines were added.</param>
/// <param name="Total">What is owed — the same figure <see cref="Domain.Invoices.Invoice.Total"/> computes.</param>
/// <remarks>
/// A projection, built from the aggregate the moment it exists rather than re-derived from a
/// second read: <c>GenerateInvoiceHandler</c> already holds the fully-lined <c>Invoice</c> in
/// memory when it returns this, so <see cref="Total"/> is the one figure this shape exists for and
/// not a second computation of it — the same reasoning that keeps <c>DispatchBoard</c> unreshaped
/// at its own read model. This is an internal shape, not a wire shape; step 49's DTOs are the
/// external ones, mapped at the edge.
/// </remarks>
public sealed record InvoiceSummary(
    InvoiceId Id,
    JobId JobId,
    InvoiceStatus Status,
    DateTimeOffset Issued,
    IReadOnlyList<InvoiceLineSummary> Lines,
    Money Total);

/// <summary>One billed line, as a reader sees it.</summary>
/// <param name="Kind">Labour or a part.</param>
/// <param name="Description">What it says on the invoice.</param>
/// <param name="Quantity">How many — hours for labour, units for parts.</param>
/// <param name="UnitPrice">The price of one. May be negative: a discount is a negative price.</param>
/// <param name="LineTotal">What this line adds to the invoice.</param>
public sealed record InvoiceLineSummary(
    LineItemKind Kind,
    string Description,
    decimal Quantity,
    Money UnitPrice,
    Money LineTotal);
