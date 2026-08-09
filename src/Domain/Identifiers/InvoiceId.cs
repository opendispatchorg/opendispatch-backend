namespace OpenDispatch.Domain.Identifiers;

/// <summary>
/// Identifies an Invoice — completed work turned into a billable document.
/// </summary>
public readonly record struct InvoiceId(Guid Value)
{
    /// <summary>Mints a new identifier for an invoice that does not exist yet.</summary>
    public static InvoiceId New() => new(Guid.NewGuid());

    /// <summary>Rebuilds an identifier from a value that came out of storage or off the wire.</summary>
    public static InvoiceId From(Guid value) => new(value);
}
