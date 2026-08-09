namespace OpenDispatch.Domain.Identifiers;

/// <summary>
/// Identifies an Organization — the tenant. Every business record carries one.
/// </summary>
public readonly record struct OrgId(Guid Value)
{
    /// <summary>Mints a new identifier for an organization that does not exist yet.</summary>
    public static OrgId New() => new(Guid.NewGuid());

    /// <summary>Rebuilds an identifier from a value that came out of storage or off the wire.</summary>
    public static OrgId From(Guid value) => new(value);
}
