namespace OpenDispatch.Domain.Identifiers;

/// <summary>
/// Identifies a Customer — the account a job is raised for.
/// </summary>
public readonly record struct CustomerId(Guid Value)
{
    /// <summary>Mints a new identifier for a customer that does not exist yet.</summary>
    public static CustomerId New() => new(Guid.NewGuid());

    /// <summary>Rebuilds an identifier from a value that came out of storage or off the wire.</summary>
    public static CustomerId From(Guid value) => new(value);
}
