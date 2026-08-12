namespace OpenDispatch.Contracts.Invoicing;

/// <summary>One billed line, as the clients see it.</summary>
/// <param name="Kind">Labour or a part.</param>
/// <param name="Description">What it says on the invoice.</param>
/// <param name="Quantity">How many — hours for labour, units for parts.</param>
/// <param name="UnitPrice">The price of one, in dollars. Negative for a discount.</param>
/// <param name="LineTotal">What this line adds to the invoice, in dollars.</param>
public sealed record InvoiceLineResponse(
    LineItemKind Kind,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);
