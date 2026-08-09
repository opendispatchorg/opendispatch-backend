namespace OpenDispatch.Domain.Identifiers;

/// <summary>
/// Identifies a Job — the customer demand.
/// </summary>
/// <remarks>
/// A wrapper rather than a raw <see cref="Guid"/> so the compiler rejects passing a
/// technician's id where a job's is expected, and so call sites read as what they are.
/// </remarks>
public readonly record struct JobId(Guid Value)
{
    /// <summary>Mints a new identifier for a job that does not exist yet.</summary>
    public static JobId New() => new(Guid.NewGuid());

    /// <summary>Rebuilds an identifier from a value that came out of storage or off the wire.</summary>
    public static JobId From(Guid value) => new(value);
}
