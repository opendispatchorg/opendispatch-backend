using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Domain.Common;

/// <summary>
/// What personal data is replaced with when somebody is erased.
/// </summary>
/// <remarks>
/// <para>
/// Erasure here is overwriting, not deletion: a customer's row stays, because their jobs and their
/// invoices reference it and a shop must keep the financial record it is required to keep. So every
/// field that says something about a person is replaced with a value that says nothing, and the row
/// that remains is a receipt with the name cut out of it.
/// </para>
/// <para>
/// One place rather than a literal at each site, because "is this erased" is asked in the domain,
/// the export and the tests, and a second spelling of the tombstone would make one of them wrong.
/// </para>
/// </remarks>
public static class Tombstone
{
    /// <summary>What an erased name, label or address reads as.</summary>
    /// <remarks>
    /// Deliberately not blank: every one of these fields has a "must not be empty" invariant, and a
    /// value that is visibly a tombstone is also what stops somebody reading a blank name as a bug
    /// and "fixing" it.
    /// </remarks>
    public const string Text = "[erased]";

    /// <summary>Where an erased place is.</summary>
    /// <remarks>
    /// The coordinates of somebody's home identify them as surely as the address does, so they go
    /// with it. Null Island rather than a null column: the point is not optional anywhere that
    /// holds one, and a nullable coordinate would put an "if" in the scheduler for a case the
    /// scheduler never sees — erased work is finished work.
    /// </remarks>
    public static GeoPoint Point { get; } = new(0d, 0d);
}
