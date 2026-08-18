namespace OpenDispatch.Contracts.Invoicing;

/// <summary>The body of <c>POST /jobs/{id}/invoice</c>. The job comes from the route.</summary>
/// <param name="Lines">
/// What to charge for, when the caller is stating it. Omit it — or send <c>null</c> — to bill what
/// the technician recorded against the job: their labour and their parts, exactly as they entered
/// them in the field.
/// </param>
/// <remarks>
/// Omitting the lines is the ordinary case and is what closes Document 1's "turn a completed job
/// into an invoice from its labor and parts" — the office no longer re-types what the van already
/// sent up. Supplying them still wins, for the visit whose record needs correcting, the warranty
/// call billed differently, or the fee nobody stood in a house and typed.
/// </remarks>
public sealed record CreateInvoiceRequest(IReadOnlyList<InvoiceLineRequest>? Lines = null);
