namespace OpenDispatch.Contracts.Invoicing;

/// <summary>
/// One invoice, as the clients see it — the response from <c>POST /jobs/{id}/invoice</c>, and the
/// shape every invoice takes in <c>GET /export</c>.
/// </summary>
/// <param name="Id">Its identity.</param>
/// <param name="JobId">The job it bills.</param>
/// <param name="Status">Whether it has been settled.</param>
/// <param name="Issued">When it was raised.</param>
/// <param name="Lines">What it bills for, in the order the lines were added.</param>
/// <param name="Total">What is owed, in dollars.</param>
public sealed record InvoiceResponse(
    Guid Id,
    Guid JobId,
    InvoiceStatus Status,
    DateTimeOffset Issued,
    IReadOnlyList<InvoiceLineResponse> Lines,
    decimal Total);
