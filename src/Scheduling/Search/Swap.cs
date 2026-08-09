using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.Scheduling.Search;

/// <summary>
/// Exchanges two stops, so each takes the other's place in the day.
/// </summary>
/// <remarks>
/// What <see cref="Relocate"/> cannot reach on its own. Two technicians who have each ended up
/// with the other's half of the city are one swap apart, but several relocates apart — and
/// every relocate in between makes one of the two days worse, so a search that only relocates
/// has to climb through a worse schedule to find the better one. Swapping steps over that
/// ridge in one move.
/// </remarks>
internal sealed record Swap : Move
{
    /// <summary>Exchanges two stops. They may be on the same technician's day.</summary>
    public Swap(TechnicianId left, int leftIndex, TechnicianId right, int rightIndex)
        : base(left == right ? [left] : [left, right])
    {
        RejectNegative(leftIndex, nameof(leftIndex));
        RejectNegative(rightIndex, nameof(rightIndex));

        Left = left;
        LeftIndex = leftIndex;
        Right = right;
        RightIndex = rightIndex;
    }

    /// <summary>Whose day the first stop is on.</summary>
    public TechnicianId Left { get; }

    /// <summary>Where in that day it sits.</summary>
    public int LeftIndex { get; }

    /// <summary>Whose day the second stop is on.</summary>
    public TechnicianId Right { get; }

    /// <summary>Where in that day it sits.</summary>
    public int RightIndex { get; }

    /// <inheritdoc />
    public override void RewriteIn(IReadOnlyDictionary<TechnicianId, List<SchedJob>> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);

        var left = runs[Left];
        var right = runs[Right];

        (left[LeftIndex], right[RightIndex]) = (right[RightIndex], left[LeftIndex]);
    }
}
