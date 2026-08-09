namespace OpenDispatch.Domain.Identifiers;

/// <summary>
/// Identifies a ServiceLocation — a physical address a customer wants work done at.
/// </summary>
/// <remarks>
/// A service location is owned by the Customer aggregate rather than being a root of its
/// own, but a Job points at one from outside that boundary, so it needs a real identifier
/// like any other cross-aggregate reference.
/// </remarks>
public readonly record struct ServiceLocationId(Guid Value)
{
    /// <summary>Mints a new identifier for a service location that does not exist yet.</summary>
    public static ServiceLocationId New() => new(Guid.NewGuid());

    /// <summary>Rebuilds an identifier from a value that came out of storage or off the wire.</summary>
    public static ServiceLocationId From(Guid value) => new(value);
}
