using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.Scheduling.Search;

/// <summary>
/// One rearrangement of a schedule — a step from the day you have to a neighbouring day.
/// </summary>
/// <remarks>
/// <para>
/// A move says only <em>what to rearrange</em>. Whether the result is possible, and what it
/// costs, is <see cref="SearchState"/>'s business, so a new kind of move is a new subclass and
/// nothing else in the engine changes. That is the seam the search is extended through.
/// </para>
/// <para>
/// Moves shuffle work that is already scheduled. None of them touches the unassigned pile:
/// getting a dropped job back into a day is insertion, a different problem with a different
/// shape, and folding it in here would mean every move had to reason about the biggest term
/// in the objective.
/// </para>
/// <para>
/// Indices are positions in a technician's run, counting from zero. A move that names one
/// that does not exist is a bug in whatever proposed it, not a move that happens to be
/// disallowed, and it is left to fail loudly.
/// </para>
/// </remarks>
internal abstract record Move
{
    protected Move(ImmutableArray<TechnicianId> touches) => Touches = touches;

    /// <summary>
    /// Whose days this rewrites — one technician or two. Everything else in the schedule is
    /// left alone, which is what makes re-pricing a move cheap.
    /// </summary>
    public ImmutableArray<TechnicianId> Touches { get; }

    /// <summary>
    /// Rearranges the runs in place. Only the technicians named by <see cref="Touches"/> are
    /// present, and the lists are the caller's to throw away if the result turns out to be
    /// impossible.
    /// </summary>
    public abstract void RewriteIn(IReadOnlyDictionary<TechnicianId, List<SchedJob>> runs);

    protected static void RejectNegative(int index, string name)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(name, index, "A position in a technician's day cannot be negative.");
        }
    }
}
