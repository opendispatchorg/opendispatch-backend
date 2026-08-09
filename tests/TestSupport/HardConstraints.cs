using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.Scheduling.Travel;

namespace OpenDispatch.TestSupport;

/// <summary>
/// Checks a solution against the rules no schedule may break, whatever produced it.
/// </summary>
/// <remarks>
/// <para>
/// Shared rather than written per test because every step of the engine has to keep the same
/// promises: the greedy constructor makes a feasible day, the local search must not break one
/// while improving it, and incremental insertion must not break one while adding to it. One
/// checker means a new constraint is added once and every step is held to it from then on.
/// </para>
/// <para>
/// It reports violations rather than asserting them, so it owes nothing to a test framework
/// and reads the same from any of them: <c>Assert.Empty(HardConstraints.Violations(...))</c>,
/// and a failure names what broke rather than saying <c>false is not true</c>.
/// </para>
/// <para>
/// Soft constraints are deliberately absent. A schedule that runs past a promised window is
/// legal and expensive; it is not a violation, and a checker that said otherwise would be
/// arguing with the design.
/// </para>
/// </remarks>
public static class HardConstraints
{
    /// <summary>
    /// Time comparisons are made to the millisecond. Travel arrives as a fractional number of
    /// minutes and <see cref="TimeSpan.FromMinutes(double)"/> rounds to milliseconds, so
    /// insisting on exactness would fail on the rounding rather than on the schedule.
    /// </summary>
    private static readonly TimeSpan Slack = TimeSpan.FromMilliseconds(1);

    /// <summary>
    /// Everything wrong with <paramref name="solution"/> as an answer to
    /// <paramref name="problem"/>. Empty means the schedule is workable.
    /// </summary>
    /// <param name="problem">The problem being answered.</param>
    /// <param name="solution">The schedule to check.</param>
    /// <param name="travel">
    /// The same provider the schedule was built with — a route timed against one set of
    /// distances and checked against another proves nothing.
    /// </param>
    public static ImmutableArray<string> Violations(
        SchedulingProblem problem,
        Solution solution,
        ITravelTimeProvider travel)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(travel);

        var violations = ImmutableArray.CreateBuilder<string>();
        var jobs = problem.Jobs.ToDictionary(job => job.Id);

        foreach (var (technicianId, route) in solution.Routes)
        {
            var technician = problem.Technicians.SingleOrDefault(t => t.Id == technicianId);

            if (technician is null)
            {
                violations.Add($"Route given to {technicianId.Value}, who is not in the problem.");
                continue;
            }

            CheckRoute(technician, route, jobs, travel, violations);
        }

        CheckEveryJobIsAccountedForExactlyOnce(problem, solution, violations);

        return violations.ToImmutable();
    }

    private static void CheckRoute(
        TechPlan technician,
        ImmutableArray<Stop> route,
        Dictionary<JobId, SchedJob> jobs,
        ITravelTimeProvider travel,
        ImmutableArray<string>.Builder violations)
    {
        for (var i = 0; i < route.Length; i++)
        {
            var stop = route[i];

            if (!jobs.TryGetValue(stop.JobId, out var job))
            {
                violations.Add($"{technician.Id.Value} is doing job {stop.JobId.Value}, which is not in the problem.");
                continue;
            }

            if (!technician.HasSkill(job.RequiredSkill))
            {
                violations.Add($"{technician.Id.Value} does not hold '{job.RequiredSkill}' but was given a job needing it.");
            }

            if (stop.Start + Slack < technician.Shift.Start)
            {
                violations.Add($"{technician.Id.Value} starts job {stop.JobId.Value} at {stop.Start:t}, before their shift.");
            }

            if (stop.End > technician.Shift.End + Slack)
            {
                violations.Add($"{technician.Id.Value} is still working job {stop.JobId.Value} at {stop.End:t}, past the end of their shift.");
            }

            if (stop.End - stop.Start + Slack < job.Duration)
            {
                violations.Add($"Job {stop.JobId.Value} is given {stop.Duration} on the day but takes {job.Duration}.");
            }

            if (stop.Start + Slack < job.Window.Start)
            {
                violations.Add($"Job {stop.JobId.Value} starts at {stop.Start:t}, before the window it was promised opens.");
            }

            var departure = i == 0 ? technician.Shift.Start : route[i - 1].End;
            var from = i == 0 ? technician.HomeBase : jobs[route[i - 1].JobId].Location;
            var drive = TimeSpan.FromMinutes(travel.Minutes(from, job.Location));

            if (stop.Arrival + Slack < departure + drive)
            {
                violations.Add(
                    $"{technician.Id.Value} reaches job {stop.JobId.Value} at {stop.Arrival:t}, sooner than a {drive} drive from {departure:t} allows.");
            }
        }
    }

    private static void CheckEveryJobIsAccountedForExactlyOnce(
        SchedulingProblem problem,
        Solution solution,
        ImmutableArray<string>.Builder violations)
    {
        // Solution already refuses a job placed twice or both placed and dropped. What it
        // cannot see is a job the engine simply lost: neither on anyone's day nor reported.
        var placed = solution.Routes.Values.SelectMany(route => route).Select(stop => stop.JobId);
        var accounted = placed.Concat(solution.Unassigned).ToHashSet();

        foreach (var job in problem.Jobs.Where(job => !accounted.Contains(job.Id)))
        {
            violations.Add($"Job {job.Id.Value} is neither scheduled nor reported unassigned.");
        }
    }
}
