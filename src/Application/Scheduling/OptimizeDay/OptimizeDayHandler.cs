using System.Diagnostics;
using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Observability;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.Technicians;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.Application.Scheduling.OptimizeDay;

/// <summary>
/// Hands the day to the engine and writes back what it decides.
/// </summary>
/// <remarks>
/// <para>
/// The seam the whole Scheduling project exists for. Everything above this line is a database and
/// everything below it is arithmetic: the engine is handed a plain problem, returns a plain
/// solution, and never learns whether either was persisted. That is what makes the hard part
/// testable in isolation — and what makes this handler as dull as it is.
/// </para>
/// <para>
/// <strong>It reads jobs and technicians, and writes only assignments and job statuses.</strong>
/// Re-planning a day rewrites the plan and leaves the demand where it was, which is the whole
/// reason those are separate aggregates. A job that gets planned for the first time moves to
/// <c>Scheduled</c>; nothing else about it changes.
/// </para>
/// <para>
/// The seed is fixed, so the same day yields the same plan every time it is optimised. Document 2
/// §4 requires that of the engine, and a caller that could vary it would take it back — a demo
/// that produces a different board on each run is not a demo, and a plan a dispatcher cannot
/// reproduce is one they cannot argue with.
/// </para>
/// </remarks>
internal sealed class OptimizeDayHandler(
    IJobRepository jobs,
    ITechnicianRepository technicians,
    IAssignmentRepository assignments,
    IScheduler scheduler,
    ITenantContext tenant,
    SchedulingMetrics metrics)
    : IRequestHandler<OptimizeDayCommand, Result<OptimizedDay>>
{
    public async Task<Result<OptimizedDay>> Handle(
        OptimizeDayCommand command,
        CancellationToken cancellationToken)
    {
        // Timed across the whole handler rather than around scheduler.Solve, because what grows
        // with the day is not only the search: reading a hundred jobs and staging a hundred stops
        // are part of what a dispatcher waits for. It stops short of the commit, which belongs to
        // the transaction behavior above and is a database number rather than an engine one.
        var started = Stopwatch.GetTimestamp();
        var horizon = new TimeWindow(command.From, command.To);

        // "Schedulable" is the domain's answer, not a status list written here: work that is
        // finished, abandoned or already being driven to belongs to the technician doing it.
        var schedulable = await jobs.ListSchedulableAsync(horizon, cancellationToken).ConfigureAwait(false);
        var crew = await technicians.ListAsync(cancellationToken).ConfigureAwait(false);

        // …and the hours that work is taking are gone from the day, which the engine has no way to
        // be told. See CommittedWork: without this, re-planning a day that is already under way
        // puts a fresh stop on top of the one a technician is standing at.
        var committed = await CommittedAsync(horizon, crew, schedulable, cancellationToken).ConfigureAwait(false);

        var problem = new SchedulingProblem(
            horizon,
            crew.Select(technician => ToPlan(technician, committed)),
            schedulable.Select(ToSchedJob),
            Weigh(command.Weights),
            SchedulingDefaults.Seed);

        var solution = scheduler.Solve(problem);
        var optimized = await ApplyAsync(problem, solution, schedulable, committed, cancellationToken)
            .ConfigureAwait(false);

        // Only a completed optimisation is recorded. A run that threw produced no plan and has no
        // latency worth a percentile; LoggingBehavior above already reports that it threw and how
        // long it ran before it did.
        metrics.Optimized(Stopwatch.GetElapsedTime(started));

        return Result.Success(optimized);
    }

    private static TechPlan ToPlan(
        Technician technician,
        IReadOnlyDictionary<TechnicianId, CommittedDay> committed) =>
        new(
            technician.Id,
            technician.Skills,
            committed.TryGetValue(technician.Id, out var alreadySpokenFor)
                ? CommittedWork.Remaining(technician.Shift, alreadySpokenFor)
                : technician.Shift,
            technician.HomeBase);

    /// <summary>
    /// The stops this re-plan may not move, and what they cost each technician.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stop is committed when the job behind it is not one this re-plan covers — it is under
    /// way, finished, called off, or simply promised outside the horizon being planned. Either way
    /// the row stays where it is and the hours it occupies are not the engine's to give away.
    /// </para>
    /// <para>
    /// The stops are read over the shifts as well as the horizon, because a technician who started
    /// before the stretch being re-planned is still busy with what they started: a horizon that
    /// opens at noon must not hide the eleven o'clock job that runs until half past twelve.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyDictionary<TechnicianId, CommittedDay>> CommittedAsync(
        TimeWindow horizon,
        IReadOnlyList<Technician> crew,
        IReadOnlyList<Job> schedulable,
        CancellationToken cancellationToken)
    {
        var planned = await assignments
            .ListInHorizonAsync(Widen(horizon, crew), cancellationToken)
            .ConfigureAwait(false);

        var replanning = schedulable.Select(job => job.Id).ToHashSet();
        var stops = planned.Where(stop => !replanning.Contains(stop.JobId)).ToList();

        if (stops.Count == 0)
        {
            return CommittedWork.None;
        }

        var behind = new Dictionary<JobId, Job>();

        foreach (var stop in stops)
        {
            if (behind.ContainsKey(stop.JobId))
            {
                continue;
            }

            if (await jobs.GetAsync(stop.JobId, cancellationToken).ConfigureAwait(false) is { } job)
            {
                behind[job.Id] = job;
            }
        }

        return CommittedWork.From(stops, behind);
    }

    /// <summary>The horizon, widened to cover every technician's working hours.</summary>
    private static TimeWindow Widen(TimeWindow horizon, IReadOnlyList<Technician> crew)
    {
        var widened = horizon;

        foreach (var technician in crew)
        {
            widened = new TimeWindow(
                technician.Shift.Start < widened.Start ? technician.Shift.Start : widened.Start,
                technician.Shift.End > widened.End ? technician.Shift.End : widened.End);
        }

        return widened;
    }

    private static SchedJob ToSchedJob(Job job) => new(
        job.Id,
        job.Location,
        job.RequiredSkill,
        job.Priority,
        job.Window,
        job.EstimatedDuration);

    private static Objective Weigh(ObjectiveWeights? weights) => weights is null
        ? Objective.Default
        : new Objective(weights.Travel, weights.Lateness, weights.Overtime, weights.Unassigned);

    /// <summary>
    /// Writes the plan: a stop for every job the engine placed, and no stop at all for the ones it
    /// could not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The technicians are walked in the problem's order rather than the solution's dictionary
    /// order, so two runs over the same day write the same things in the same order — which is
    /// also the order the domain events come out in.
    /// </para>
    /// <para>
    /// A job the engine could not place loses the stop it had. That is what
    /// <c>IAssignmentRepository.Remove</c> exists for and the only deletion in the system: an
    /// orphaned stop would show on the board as work somebody is expected to drive to.
    /// </para>
    /// </remarks>
    private async Task<OptimizedDay> ApplyAsync(
        SchedulingProblem problem,
        Solution solution,
        IReadOnlyList<Job> schedulable,
        IReadOnlyDictionary<TechnicianId, CommittedDay> committed,
        CancellationToken cancellationToken)
    {
        var byId = schedulable.ToDictionary(job => job.Id);
        var planned = 0;

        foreach (var technician in problem.Technicians)
        {
            var route = solution.RouteFor(technician.Id);

            // A re-planned run continues the numbering of the stops it may not touch, rather than
            // starting again at zero beside them — two stops sharing a position in one day is a
            // board nobody can read and an order nobody can drive.
            var first = committed.TryGetValue(technician.Id, out var alreadySpokenFor)
                ? alreadySpokenFor.NextSequence
                : 0;

            for (var sequence = 0; sequence < route.Length; sequence++)
            {
                var stop = route[sequence];

                await StopPlacement.PlaceAsync(
                    assignments,
                    tenant.OrgId,
                    byId[stop.JobId],
                    technician.Id,
                    first + sequence,
                    // Start, not Arrival — and the two differ only when a technician reaches a
                    // site before the customer's window opens and waits. Start is what a
                    // dispatcher enters by hand for the same stop, what the customer was promised,
                    // and the instant lateness is measured at (a job begun inside its window has
                    // kept the promise, however long it then runs). Recording the arrival would
                    // make the manual and optimised paths mean different things by the same name,
                    // and would make lateness underivable from the board.
                    stop.Start,
                    stop.TravelMin,
                    cancellationToken).ConfigureAwait(false);

                planned++;
            }
        }

        foreach (var dropped in solution.Unassigned)
        {
            var orphan = await assignments.GetByJobAsync(dropped, cancellationToken).ConfigureAwait(false);

            if (orphan is not null)
            {
                assignments.Remove(orphan);
            }

            // The other half of dropping the stop. A job whose plan has been withdrawn is demand
            // again, and saying so is what keeps the board honest: without it the job sits in the
            // unassigned pile still labelled Scheduled, which is the one thing on that screen that
            // would be false. Only work that was planned has anything to withdraw — a job that was
            // already waiting is left alone.
            if (byId[dropped].Status is JobStatus.Scheduled or JobStatus.Dispatched)
            {
                byId[dropped].Unschedule();
            }
        }

        return new OptimizedDay(planned, solution.Unassigned, solution.Cost);
    }
}
