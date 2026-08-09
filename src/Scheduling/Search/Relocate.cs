using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.Scheduling.Search;

/// <summary>
/// Lifts one stop out of a day and drops it somewhere else — later in the same run, or onto
/// another technician entirely.
/// </summary>
/// <remarks>
/// The workhorse. It is the only move that changes how much work each technician has, so it is
/// the one that can undo a bad decision the constructor made about <em>who</em> rather than
/// about <em>when</em> — and the only one that can empty a technician's day so another can
/// take the whole cluster.
/// </remarks>
internal sealed record Relocate : Move
{
    /// <summary>Moves the stop at <paramref name="fromIndex"/> on <paramref name="from"/>'s day.</summary>
    /// <param name="from">Whose day the stop is leaving.</param>
    /// <param name="fromIndex">Where in that day it currently sits.</param>
    /// <param name="to">Whose day it is joining. May be the same technician.</param>
    /// <param name="toIndex">
    /// Where it lands, counted in the run <em>after</em> the stop has been lifted out. Without
    /// that rule a relocate within one day would mean two different things depending on which
    /// direction it moved.
    /// </param>
    public Relocate(TechnicianId from, int fromIndex, TechnicianId to, int toIndex)
        : base(from == to ? [from] : [from, to])
    {
        RejectNegative(fromIndex, nameof(fromIndex));
        RejectNegative(toIndex, nameof(toIndex));

        From = from;
        FromIndex = fromIndex;
        To = to;
        ToIndex = toIndex;
    }

    /// <summary>Whose day the stop is leaving.</summary>
    public TechnicianId From { get; }

    /// <summary>Where in that day it currently sits.</summary>
    public int FromIndex { get; }

    /// <summary>Whose day it is joining.</summary>
    public TechnicianId To { get; }

    /// <summary>Where it lands, counted after it has been lifted out.</summary>
    public int ToIndex { get; }

    /// <inheritdoc />
    public override void RewriteIn(IReadOnlyDictionary<TechnicianId, List<SchedJob>> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var leaving = runs[From];
        var job = leaving[FromIndex];

        leaving.RemoveAt(FromIndex);
        runs[To].Insert(ToIndex, job);
    }
}
