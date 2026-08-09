using System.Collections.Immutable;
using System.Diagnostics;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.Scheduling.Search;

/// <summary>
/// Suggests rearrangements to try, at random but reproducibly.
/// </summary>
/// <remarks>
/// <para>
/// The search needs somewhere to go next, and any of the three kinds of move might be the one
/// that helps, so the choice is a coin toss rather than a judgement — judging a move before
/// trying it would mean pricing it, which is the expensive part anyway.
/// </para>
/// <para>
/// Stops are drawn evenly from across the whole schedule rather than by picking a technician
/// first: a technician with eight jobs has eight times as much that could be improved as one
/// with a single job, and picking the person first would give the quiet day the same attention
/// as the busy one.
/// </para>
/// <para>
/// It returns nothing when the schedule cannot support the move it drew — a reversal needs two
/// stops on one day, a swap needs two distinct stops anywhere. The caller treats that as an
/// iteration that came to nothing, which keeps the random sequence, and therefore the whole
/// search, identical from one run to the next.
/// </para>
/// </remarks>
internal sealed class MoveGenerator
{
    private const int MoveKinds = 3;

    private readonly ImmutableArray<TechnicianId> _technicians;
    private readonly Random _random;

    /// <summary>Draws moves for <paramref name="problem"/>'s technicians from <paramref name="random"/>.</summary>
    public MoveGenerator(SchedulingProblem problem, Random random)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(random);

        _technicians = [.. problem.Technicians.Select(technician => technician.Id)];
        _random = random;
    }

    /// <summary>
    /// One rearrangement to consider, or nothing if the schedule as it stands cannot support
    /// the kind of move that came up.
    /// </summary>
    public Move? Propose(SearchState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return _random.Next(MoveKinds) switch
        {
            0 => ProposeRelocate(state),
            1 => ProposeSwap(state),
            _ => ProposeTwoOpt(state),
        };
    }

    private Relocate? ProposeRelocate(SearchState state)
    {
        if (!TryPickStop(state, out var from, out var fromIndex))
        {
            return null;
        }

        var to = _technicians[_random.Next(_technicians.Length)];

        // Counted in the day the stop has already been lifted out of, which is one shorter
        // when it is being put back on the same technician.
        var room = state.RunOf(to).Count - (from == to ? 1 : 0);

        return new Relocate(from, fromIndex, to, _random.Next(room + 1));
    }

    private Swap? ProposeSwap(SearchState state)
    {
        if (!TryPickStop(state, out var left, out var leftIndex) ||
            !TryPickStop(state, out var right, out var rightIndex))
        {
            return null;
        }

        // Exchanging a stop with itself is not a rearrangement.
        return left == right && leftIndex == rightIndex
            ? null
            : new Swap(left, leftIndex, right, rightIndex);
    }

    private TwoOpt? ProposeTwoOpt(SearchState state)
    {
        if (!TryPickStop(state, out var technician, out _))
        {
            return null;
        }

        var length = state.RunOf(technician).Count;
        if (length < 2)
        {
            return null;
        }

        var from = _random.Next(length - 1);

        return new TwoOpt(technician, from, _random.Next(from + 1, length));
    }

    private bool TryPickStop(SearchState state, out TechnicianId technician, out int index)
    {
        var scheduled = 0;

        foreach (var candidate in _technicians)
        {
            scheduled += state.RunOf(candidate).Count;
        }

        if (scheduled == 0)
        {
            technician = default;
            index = default;

            return false;
        }

        var target = _random.Next(scheduled);

        foreach (var candidate in _technicians)
        {
            var length = state.RunOf(candidate).Count;

            if (target < length)
            {
                technician = candidate;
                index = target;

                return true;
            }

            target -= length;
        }

        throw new UnreachableException();
    }
}
