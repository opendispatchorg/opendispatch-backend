using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace OpenDispatch.Infrastructure.Persistence.Conversions;

/// <summary>
/// Stores a technician's skills as one <c>text[]</c> column, and rebuilds the set
/// case-insensitively on the way back.
/// </summary>
/// <remarks>
/// <para>
/// The comparer is the whole point of this file. A technician's skills are compared without
/// regard to case — "HVAC" matches "hvac" — and that lives on the <see cref="HashSet{T}"/>'s
/// comparer, not on the strings. Rebuild the set with the default comparer and matching silently
/// becomes case-sensitive: no error, no exception, just a job that no technician can be assigned
/// to. So the set is rebuilt with <see cref="StringComparer.OrdinalIgnoreCase"/> here, and again
/// in <c>Technician</c>'s materialisation constructor.
/// </para>
/// <para>
/// A column rather than a side table because skills are a handful of short strings read whenever
/// a technician is read, and never on their own. Postgres indexes a <c>text[]</c> with GIN if a
/// "who can do X" query ever wants one, without the join.
/// </para>
/// <para>
/// Sorted on the way out so the stored array is a function of the set and not of its enumeration
/// order — otherwise two identical skill sets produce different rows and every diff of a seeded
/// database is noise.
/// </para>
/// </remarks>
internal sealed class SkillSetConverter()
    : ValueConverter<IReadOnlySet<string>, string[]>(
        skills => skills.OrderBy(skill => skill, StringComparer.OrdinalIgnoreCase).ToArray(),
        stored => new HashSet<string>(stored, StringComparer.OrdinalIgnoreCase))
{
}

/// <summary>
/// Tells EF how to compare, hash and snapshot a skill set, since a set is a mutable reference
/// type that change tracking would otherwise compare by identity.
/// </summary>
/// <remarks>
/// The hash combines with XOR rather than <see cref="HashCode"/> because a set has no order and
/// an order-sensitive hash would give the same set two different hashes. Equality and the
/// snapshot both go through the case-insensitive comparer, so adding "HVAC" to a technician who
/// already has "hvac" is correctly seen as no change at all.
/// </remarks>
internal sealed class SkillSetComparer()
    : ValueComparer<IReadOnlySet<string>>(
        (left, right) => left != null && right != null && left.SetEquals(right),
        skills => skills.Aggregate(
            0,
            (hash, skill) => hash ^ StringComparer.OrdinalIgnoreCase.GetHashCode(skill)),
        skills => new HashSet<string>(skills, StringComparer.OrdinalIgnoreCase))
{
}
