using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Domain.Invoices;

/// <summary>
/// One billed line: so many hours of labour, or so many of a part, at a price each.
/// </summary>
/// <remarks>
/// Owned by <see cref="Invoice"/>, and only ever created through it. The line total is
/// computed rather than stored — a stored total is a second source of truth that can drift
/// from the quantity and price beside it.
/// </remarks>
public sealed class LineItem
{
    // Materialisation constructor — see the note on Job.
    private LineItem() => Description = string.Empty;

    private LineItem(
        LineItemId id,
        LineItemKind kind,
        string description,
        decimal quantity,
        Money unitPrice)
    {
        Id = id;
        Kind = kind;
        Description = description;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    /// <summary>This line's identity within its invoice.</summary>
    public LineItemId Id { get; private set; }

    /// <summary>Whether the line is labour or a part.</summary>
    public LineItemKind Kind { get; private set; }

    /// <summary>What it says on the invoice.</summary>
    public string Description { get; private set; }

    /// <summary>How many — hours for labour, units for parts. Fractional quantities are ordinary.</summary>
    public decimal Quantity { get; private set; }

    /// <summary>
    /// The price of one. May be negative: a discount or an adjustment is a line with a
    /// negative price, not a negative quantity.
    /// </summary>
    public Money UnitPrice { get; private set; }

    /// <summary>What this line adds to the invoice, rounded to the cent.</summary>
    public Money LineTotal => UnitPrice.Multiply(Quantity);

    /// <summary>
    /// Creates a line. Internal because only <see cref="Invoice"/> may add one.
    /// </summary>
    /// <exception cref="DomainException">The line says nothing, or bills nothing.</exception>
    internal static LineItem Create(LineItemKind kind, string description, decimal quantity, Money unitPrice)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            throw new DomainException("A billed line must say what it is for.");
        }

        if (quantity <= 0m)
        {
            throw new DomainException("A billed line must be for a positive quantity.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"'{kind}' is not a kind of line an invoice can carry.");
        }

        return new LineItem(LineItemId.New(), kind, description.Trim(), quantity, unitPrice);
    }
}
