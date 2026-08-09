namespace OpenDispatch.Domain.Identifiers;

/// <summary>
/// Identifies an Assignment — the scheduling plan for a job, kept deliberately separate
/// from the job itself.
/// </summary>
public readonly record struct AssignmentId(Guid Value)
{
    /// <summary>Mints a new identifier for an assignment that does not exist yet.</summary>
    public static AssignmentId New() => new(Guid.NewGuid());

    /// <summary>Rebuilds an identifier from a value that came out of storage or off the wire.</summary>
    public static AssignmentId From(Guid value) => new(value);
}
