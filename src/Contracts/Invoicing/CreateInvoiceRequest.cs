namespace OpenDispatch.Contracts.Invoicing;

/// <summary>The body of <c>POST /jobs/{id}/invoice</c>. The job comes from the route.</summary>
/// <param name="Lines">What to charge for — time on the job, and parts fitted.</param>
public sealed record CreateInvoiceRequest(IReadOnlyList<InvoiceLineRequest> Lines);
