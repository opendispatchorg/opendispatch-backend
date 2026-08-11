namespace OpenDispatch.Domain.Identifiers;

/// <summary>
/// Identifies a JobLine — one thing a technician recorded the work taking.
/// </summary>
/// <remarks>
/// Job lines are owned by their job and nothing outside it points at one, so this is identity
/// within the aggregate rather than a cross-boundary reference. It is still a typed id, for the
/// reason <see cref="LineItemId"/> gives: a bare <see cref="Guid"/> on a signature is the thing
/// that ends up holding the wrong one.
/// </remarks>
public readonly record struct JobLineId(Guid Value)
{
    /// <summary>Mints a new identifier for a line that does not exist yet.</summary>
    public static JobLineId New() => new(Guid.NewGuid());

    /// <summary>Rebuilds an identifier from a value that came out of storage or off the wire.</summary>
    public static JobLineId From(Guid value) => new(value);
}
