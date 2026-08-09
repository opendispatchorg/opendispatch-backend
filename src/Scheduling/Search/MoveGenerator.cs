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
/// A move that hands somebody work they are not qualified for is redrawn rather than returned.
/// Drawing blind wasted most of the search: on a shop with specialised technicians, 38% of
/// proposals came back from <see cref="SearchState.Try"/> impossible for that reason alone, and
/// checking a skill costs nothing next to timing a route. It lifted usable proposals there from
/// 47% to 77%.
/// </para>
/// <para>
/// The check is deliberately only one that is <em>strictly necessary</em>: everything it rules
/// out really was impossible. Nothing here decides a move is a bad idea — that is the
/// objective's job, and a generator that started making that call would be quietly narrowing
/// the search to what it already expected.
/// </para>
/// <para>
/// The other reason a proposal fails is a day with no room left, and that one is not worth
/// pre-empting. It accounts for most rejections on a densely booked day, but what fills those
/// days is driving and waiting rather than the work itself, so the only test cheap enough to
/// apply here — does the raw work still fit inside the shift? — almost never fires. Measured at
/// half a percentage point, for a concept the state would have had to carry.
/// </para>
/// <para>
/// It returns nothing when the schedule cannot support the move it drew — a reversal needs two
/// stops on one day, a swap needs two distinct stops anywhere — or when several attempts all
/// drew something impossible. The caller treats that as an iteration that came to nothing,
/// which keeps the random sequence, and therefore the whole search, identical from one run to
/// the next.
/// </para>
/// </remarks>
internal sealed class MoveGenerator
{
    private const int MoveKinds = 3;

    /// <summary>
    /// How many times to redraw before giving up on an iteration. Small on purpose: on a day
    /// so full that a dozen draws all fail, the neighbourhood really is nearly exhausted, and
    /// spinning here would cost more than the iteration is worth.
    /// </summary>
    private const int Attempts = 12;

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
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (!TryPickStop(state, out var from, out var fromIndex))
            {
                return null;
            }

            var moving = state.RunOf(from)[fromIndex];
            var to = _technicians[_random.Next(_technicians.Length)];

            if (from != to && !state.HasSkill(to, moving.RequiredSkill))
            {
                continue;
            }

            // Counted in the day the stop has already been lifted out of, which is one shorter
            // when it is being put back on the same technician.
            var room = state.RunOf(to).Count - (from == to ? 1 : 0);

            return new Relocate(from, fromIndex, to, _random.Next(room + 1));
        }

        return null;
    }

    private Swap? ProposeSwap(SearchState state)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            if (!TryPickStop(state, out var left, out var leftIndex) ||
                !TryPickStop(state, out var right, out var rightIndex))
            {
                return null;
            }

            // Exchanging a stop with itself is not a rearrangement.
            if (left == right && leftIndex == rightIndex)
            {
                continue;
            }

            // Each takes on the other's job, so each needs the skill for it.
            if (left != right &&
                (!state.HasSkill(left, state.RunOf(right)[rightIndex].RequiredSkill) ||
                 !state.HasSkill(right, state.RunOf(left)[leftIndex].RequiredSkill)))
            {
                continue;
            }

            return new Swap(left, leftIndex, right, rightIndex);
        }

        return null;
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
