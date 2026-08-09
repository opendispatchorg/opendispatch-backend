namespace OpenDispatch.Domain.Identifiers;

/// <summary>
/// Identifies a Technician — the resource the scheduler assigns work to.
/// </summary>
public readonly record struct TechnicianId(Guid Value)
{
    /// <summary>Mints a new identifier for a technician that does not exist yet.</summary>
    public static TechnicianId New() => new(Guid.NewGuid());

    /// <summary>Rebuilds an identifier from a value that came out of storage or off the wire.</summary>
    public static TechnicianId From(Guid value) => new(value);
}
