namespace OpenDispatch.Contracts.Invoicing;

/// <summary>One line to bill, as submitted to <c>POST /jobs/{id}/invoice</c>.</summary>
/// <param name="Kind">Labour or a part.</param>
/// <param name="Description">What it says on the invoice.</param>
/// <param name="Quantity">How many — hours for labour, units for parts. Fractions are ordinary.</param>
/// <param name="UnitPrice">
/// The price of one, in whole currency units (dollars, not cents). Negative for a discount.
/// </param>
public sealed record InvoiceLineRequest(LineItemKind Kind, string Description, decimal Quantity, decimal UnitPrice);
