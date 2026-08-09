namespace OpenDispatch.Domain.Identifiers;

/// <summary>
/// Identifies a LineItem — one billed line on an invoice.
/// </summary>
/// <remarks>
/// Line items are owned by their invoice and nothing outside it points at one, so this
/// exists for identity within the aggregate rather than as a cross-boundary reference.
/// It is still a typed id: a bare <see cref="Guid"/> on a method signature is exactly the
/// thing that ends up holding an invoice id one day.
/// </remarks>
public readonly record struct LineItemId(Guid Value)
{
    /// <summary>Mints a new identifier for a line that does not exist yet.</summary>
    public static LineItemId New() => new(Guid.NewGuid());

    /// <summary>Rebuilds an identifier from a value that came out of storage or off the wire.</summary>
    public static LineItemId From(Guid value) => new(value);
}
