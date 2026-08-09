using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.Scheduling.Travel;

namespace OpenDispatch.Scheduling.Search;

/// <summary>
/// A schedule part-way through being improved: the order of each technician's day, the jobs
/// nobody has, and what all of it currently costs.
/// </summary>
/// <remarks>
/// <para>
/// The working copy. A <see cref="Solution"/> is timed and immutable — right for an answer,
/// wrong for a search that rewrites a route thousands of times a second — so this holds the
/// <em>order</em> of each day, derives the timings, and keeps each route's price cached beside
/// it.
/// </para>
/// <para>
/// Moves are proposed and inspected before they are taken: <see cref="Try"/> works out what a
/// move would cost and changes nothing, <see cref="Commit"/> takes it. That is the shape
/// annealing needs — it has to see the price before deciding whether to accept a worse day —
/// and it means there is no undo to get wrong.
/// </para>
/// <para>
/// Only the routes a move touches are re-timed and re-priced; every other day keeps the price
/// it already had. The total is then added up afresh from those cached prices rather than
/// nudged by a difference — summing a couple of dozen numbers costs nothing next to re-timing
/// a route, and a running total adjusted a million times drifts away from the truth by
/// arithmetic that no test would ever catch.
/// </para>
/// <para>
/// The unassigned pile does not move. None of the moves can reach it, so its price is worked
/// out once and carried.
/// </para>
/// </remarks>
internal sealed class SearchState
{
    private readonly SchedulingProblem _problem;
    private readonly TravelMatrix _distances;
    private readonly ObjectiveEvaluator _objective;
    private readonly Dictionary<TechnicianId, TechPlan> _technicians;
    private readonly Dictionary<TechnicianId, List<SchedJob>> _runs;
    private readonly Dictionary<TechnicianId, RouteState> _routes = [];
    private readonly ImmutableArray<JobId> _unassigned;
    private readonly Cost _unassignedCost;

    private SearchState(
        SchedulingProblem problem,
        TravelMatrix distances,
        Dictionary<TechnicianId, List<SchedJob>> runs,
        ImmutableArray<JobId> unassigned)
    {
        _problem = problem;
        _distances = distances;
        _objective = new ObjectiveEvaluator(problem, distances);
        _technicians = problem.Technicians.ToDictionary(technician => technician.Id);
        _runs = runs;
        _unassigned = unassigned;
        _unassignedCost = _objective.EvaluateUnassigned(unassigned);

        foreach (var technician in problem.Technicians)
        {
            if (Price(technician, runs[technician.Id]) is not { } route)
            {
                throw new ArgumentException(
                    $"{technician.Id.Value} cannot drive the day they were given, so there is nothing here to improve on.",
                    nameof(runs));
            }

            _routes[technician.Id] = route;
        }

        Cost = Total(replacing: null);
    }

    /// <summary>What the schedule costs as it stands.</summary>
    public Cost Cost { get; private set; }

    /// <summary>Starts a search from a schedule somebody else built.</summary>
    /// <exception cref="ArgumentException">
    /// The schedule is not workable to begin with — somebody has been given a job they cannot
    /// do, or a day that does not fit their shift.
    /// </exception>
    public static SearchState From(SchedulingProblem problem, TravelMatrix distances, Solution solution)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(solution);

        var jobs = problem.Jobs.ToDictionary(job => job.Id);

        return new SearchState(
            problem,
            distances,
            problem.Technicians.ToDictionary(
                technician => technician.Id,
                technician => solution.RouteFor(technician.Id).Select(stop => jobs[stop.JobId]).ToList()),
            solution.Unassigned);
    }

    /// <summary>The order <paramref name="technician"/> is currently doing their day in.</summary>
    public IReadOnlyList<SchedJob> RunOf(TechnicianId technician) => _runs[technician];

    /// <summary>Whether <paramref name="technician"/> is qualified for this kind of work.</summary>
    /// <remarks>
    /// Exposed for the move generator, which uses it to avoid proposing rearrangements that
    /// were never going to be allowed.
    /// </remarks>
    public bool HasSkill(TechnicianId technician, string skill) => _technicians[technician].HasSkill(skill);

    /// <summary>
    /// Works out what the schedule would cost if <paramref name="move"/> were taken, without
    /// taking it.
    /// </summary>
    /// <returns>
    /// The outcome to hand to <see cref="Commit"/>, or <see langword="null"/> if the move
    /// would leave somebody with a day they cannot drive.
    /// </returns>
    public MoveOutcome? Try(Move move)
    {
        ArgumentNullException.ThrowIfNull(move);

        // Copies, so a move that turns out to be impossible has changed nothing. The copying
        // is bounded by the length of the one or two days a move can touch, not by the size
        // of the schedule.
        var candidates = move.Touches.ToDictionary(
            technician => technician,
            technician => new List<SchedJob>(_runs[technician]));

        move.RewriteIn(candidates);

        var rewrites = ImmutableArray.CreateBuilder<RouteRewrite>(move.Touches.Length);
        var replacements = new Dictionary<TechnicianId, RouteState>(move.Touches.Length);

        foreach (var technician in move.Touches)
        {
            var run = candidates[technician];

            if (Price(_technicians[technician], run) is not { } route)
            {
                return null;
            }

            replacements[technician] = route;
            rewrites.Add(new RouteRewrite(technician, run, route.Stops, route.Cost));
        }

        return new MoveOutcome(Total(replacements), rewrites.DrainToImmutable());
    }

    /// <summary>Takes a move that <see cref="Try"/> has already worked out.</summary>
    public void Commit(MoveOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        foreach (var rewrite in outcome.Rewrites)
        {
            _runs[rewrite.Technician] = rewrite.Run;
            _routes[rewrite.Technician] = new RouteState(rewrite.Stops, rewrite.Cost);
        }

        Cost = outcome.Cost;
    }

    /// <summary>The schedule as an answer, ready to leave the engine.</summary>
    public Solution ToSolution() => new(
        _problem.Technicians.ToDictionary(
            technician => technician.Id,
            technician => _routes[technician.Id].Stops),
        _unassigned,
        Cost.Total);

    private RouteState? Price(TechPlan technician, List<SchedJob> run) =>
        RouteTimer.Time(technician, run, _distances) is { } stops
            ? new RouteState(stops, _objective.EvaluateRoute(technician, stops))
            : null;

    // Over the problem's own technician order, so the sum is the same every time it is taken.
    private Cost Total(IReadOnlyDictionary<TechnicianId, RouteState>? replacing)
    {
        var cost = _unassignedCost;

        foreach (var technician in _problem.Technicians)
        {
            var route = replacing is not null && replacing.TryGetValue(technician.Id, out var candidate)
                ? candidate
                : _routes[technician.Id];

            cost = cost.Plus(route.Cost);
        }

        return cost;
    }

    /// <summary>A technician's day, timed, with what it costs.</summary>
    private sealed record RouteState(ImmutableArray<Stop> Stops, Cost Cost);
}
