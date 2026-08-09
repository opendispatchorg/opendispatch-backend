using System.Collections.Frozen;
using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.Scheduling.Travel;

namespace OpenDispatch.Scheduling;

/// <summary>
/// Prices a schedule: what the driving, the broken promises, the late finishes and the
/// undone work add up to.
/// </summary>
/// <remarks>
/// <para>
/// The only judge in the engine. Everything that searches for a better day does so by asking
/// this whether one day beats another, so it is the single place the trade-offs live — change
/// what a minute of lateness is worth here, and every part of the engine changes its mind
/// together.
/// </para>
/// <para>
/// Nothing it measures is a constraint. A window that has been missed is charged for, not
/// refused, because a real dispatcher would rather see a job an hour late than be told it
/// cannot be done. What genuinely cannot be done never reaches this: those jobs arrive in
/// <see cref="Solution.Unassigned"/> and are charged for too, at a price that makes dropping
/// one the last resort rather than a cheap way out.
/// </para>
/// <para>
/// Bound to one problem, because that is where the prices and the promises are. Handed a
/// solution to a different problem it says so, rather than quietly costing the wrong day.
/// </para>
/// </remarks>
public sealed class ObjectiveEvaluator
{
    private readonly SchedulingProblem _problem;
    private readonly TravelMatrix _distances;
    private readonly FrozenDictionary<TechnicianId, TechPlan> _technicians;
    private readonly FrozenDictionary<JobId, SchedJob> _jobs;

    /// <summary>Prices schedules that answer <paramref name="problem"/>.</summary>
    /// <param name="problem">The problem being answered — the source of the weights and the promises.</param>
    /// <param name="travel">
    /// The same provider the schedule was built with. A day timed against one set of drives
    /// and priced against another is not being measured, it is being guessed at.
    /// </param>
    public ObjectiveEvaluator(SchedulingProblem problem, ITravelTimeProvider travel)
        : this(problem, Distances(problem, travel))
    {
    }

    internal ObjectiveEvaluator(SchedulingProblem problem, TravelMatrix distances)
    {
        _problem = problem;
        _distances = distances;
        _technicians = problem.Technicians.ToFrozenDictionary(technician => technician.Id);
        _jobs = problem.Jobs.ToFrozenDictionary(job => job.Id);
    }

    /// <summary>What <paramref name="solution"/> costs, broken down by term.</summary>
    /// <exception cref="ArgumentException">
    /// The solution answers a different problem — it names a technician or a job this one does
    /// not have.
    /// </exception>
    public Cost Evaluate(Solution solution)
    {
        ArgumentNullException.ThrowIfNull(solution);

        foreach (var technician in solution.Routes.Keys)
        {
            if (!_technicians.ContainsKey(technician))
            {
                throw new ArgumentException(
                    $"The solution gives work to {technician.Value}, who is not in this problem.",
                    nameof(solution));
            }
        }

        return Evaluate(solution.Routes, solution.Unassigned);
    }

    /// <summary>
    /// What a schedule costs before there is a <see cref="Solution"/> to hold it — for
    /// whatever is still building one.
    /// </summary>
    /// <remarks>
    /// The sum runs over the technicians in the problem's own order rather than over the
    /// dictionary's. Adding doubles is not associative, so an iteration order the runtime
    /// chooses is an iteration order that could change the answer, and the same day has to
    /// cost the same every time it is priced.
    /// </remarks>
    internal Cost Evaluate(
        IReadOnlyDictionary<TechnicianId, ImmutableArray<Stop>> routes,
        IEnumerable<JobId> unassigned)
    {
        var cost = EvaluateUnassigned(unassigned);

        foreach (var technician in _problem.Technicians)
        {
            var stops = routes.TryGetValue(technician.Id, out var route) ? route : [];
            cost = cost.Plus(EvaluateRoute(technician, stops));
        }

        return cost;
    }

    /// <summary>What one technician's day costs.</summary>
    /// <remarks>
    /// The unit the search re-prices. A move rewrites at most two routes, so re-costing those
    /// and leaving the rest of the schedule alone is all the incremental evaluation the shape
    /// of these moves allows — within a route there is no cheaper honest answer, because a
    /// stop that shifts by ten minutes drags everything after it.
    /// </remarks>
    internal Cost EvaluateRoute(TechPlan technician, ImmutableArray<Stop> stops)
    {
        if (stops.IsEmpty)
        {
            return Cost.Zero;
        }

        var weights = _problem.Weights;
        var lateMinutes = 0d;

        foreach (var stop in stops)
        {
            // Measured at the start of the work rather than its end: the promise was that
            // somebody would turn up between these hours, and a job that starts inside its
            // window and overruns has been kept.
            var late = stop.Start - _jobs[stop.JobId].Window.End;
            if (late > TimeSpan.Zero)
            {
                lateMinutes += late.TotalMinutes;
            }
        }

        // Work finishing inside the shift is a hard constraint, so the only way to still be
        // out past the end of one is the drive home — which is exactly the overtime a
        // technician notices and gets paid for.
        var lateHome = RouteTimer.HomeAgain(technician, stops, _distances) - technician.Shift.End;

        return new Cost(
            Travel: weights.Travel * RouteTimer.TravelMinutes(technician, stops, _distances),
            Lateness: weights.Lateness * lateMinutes,
            Overtime: weights.Overtime * (lateHome > TimeSpan.Zero ? lateHome.TotalMinutes : 0d),
            Unassigned: 0d);
    }

    /// <summary>What the undone work costs.</summary>
    /// <remarks>
    /// Scaled by priority, so dropping an emergency costs four times dropping something that
    /// could wait. The weight is large enough that leaving a job undone is never the cheap
    /// answer — but it is a price rather than a bar, which is what lets a dispatcher see the
    /// job and decide instead of the engine refusing to produce a day at all.
    /// </remarks>
    internal Cost EvaluateUnassigned(IEnumerable<JobId> unassigned)
    {
        var penalty = 0d;

        foreach (var job in unassigned)
        {
            if (!_jobs.TryGetValue(job, out var dropped))
            {
                throw new ArgumentException(
                    $"Job {job.Value} is reported unassigned but is not in this problem.", nameof(unassigned));
            }

            penalty += _problem.Weights.Unassigned * (int)dropped.Priority;
        }

        return new Cost(Travel: 0d, Lateness: 0d, Overtime: 0d, Unassigned: penalty);
    }

    // Checked here rather than in the constructor body: the arguments are evaluated before
    // the chained constructor runs, so a null would otherwise surface from inside
    // TravelMatrix with nothing useful to say about which argument was missing.
    private static TravelMatrix Distances(SchedulingProblem problem, ITravelTimeProvider travel)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(travel);

        return TravelMatrix.For(problem, travel);
    }
}
