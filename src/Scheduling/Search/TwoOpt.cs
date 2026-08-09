using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.Scheduling.Search;

/// <summary>
/// Drives a stretch of one technician's day backwards, leaving the rest of it alone.
/// </summary>
/// <remarks>
/// <para>
/// The classic untangler. A route that crosses itself — out east, back west, out east again —
/// is always longer than the same stops driven in an order that does not, and reversing the
/// segment between the two crossing legs is exactly what unpicks it. Nothing is added or
/// removed; the technician simply drives part of their day the other way round.
/// </para>
/// <para>
/// It is worth having alongside <see cref="Relocate"/> because a crossing takes many relocates
/// to undo one stop at a time, and the intermediate days are worse than either end. One
/// reversal gets there in a step.
/// </para>
/// </remarks>
internal sealed record TwoOpt : Move
{
    /// <summary>Reverses the run between two positions, both included.</summary>
    /// <param name="technician">Whose day is being untangled.</param>
    /// <param name="from">The first stop of the stretch to reverse.</param>
    /// <param name="to">The last stop of it. Must be later than <paramref name="from"/> — reversing one stop changes nothing.</param>
    public TwoOpt(TechnicianId technician, int from, int to)
        : base([technician])
    {
        RejectNegative(from, nameof(from));

        if (to <= from)
        {
            throw new ArgumentOutOfRangeException(
                nameof(to), to, "A reversal has to span at least two stops to be a rearrangement at all.");
        }

        Technician = technician;
        From = from;
        To = to;
    }

    /// <summary>Whose day is being untangled.</summary>
    public TechnicianId Technician { get; }

    /// <summary>The first stop of the reversed stretch.</summary>
    public int From { get; }

    /// <summary>The last stop of the reversed stretch.</summary>
    public int To { get; }

    /// <inheritdoc />
    public override void RewriteIn(IReadOnlyDictionary<TechnicianId, List<SchedJob>> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        runs[Technician].Reverse(From, To - From + 1);
    }
}
