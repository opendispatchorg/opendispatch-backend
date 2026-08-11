using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Results;
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
    ITenantContext tenant)
    : IRequestHandler<OptimizeDayCommand, Result<OptimizedDay>>
{
    public async Task<Result<OptimizedDay>> Handle(
        OptimizeDayCommand command,
        CancellationToken cancellationToken)
    {
        var horizon = new TimeWindow(command.From.ToUniversalTime(), command.To.ToUniversalTime());

        // "Schedulable" is the domain's answer, not a status list written here: work that is
        // finished, abandoned or already being driven to belongs to the technician doing it.
        var schedulable = await jobs.ListSchedulableAsync(horizon, cancellationToken).ConfigureAwait(false);
        var crew = await technicians.ListAsync(cancellationToken).ConfigureAwait(false);

        var problem = new SchedulingProblem(
            horizon,
            crew.Select(ToPlan),
            schedulable.Select(ToSchedJob),
            Weigh(command.Weights),
            SchedulingDefaults.Seed);

        var solution = scheduler.Solve(problem);

        return Result.Success(await ApplyAsync(problem, solution, schedulable, cancellationToken).ConfigureAwait(false));
    }

    private static TechPlan ToPlan(Technician technician) =>
        new(technician.Id, technician.Skills, technician.Shift, technician.HomeBase);

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
        CancellationToken cancellationToken)
    {
        var byId = schedulable.ToDictionary(job => job.Id);
        var planned = 0;

        foreach (var technician in problem.Technicians)
        {
            var route = solution.RouteFor(technician.Id);

            for (var sequence = 0; sequence < route.Length; sequence++)
            {
                var stop = route[sequence];

                await StopPlacement.PlaceAsync(
                    assignments,
                    tenant.OrgId,
                    byId[stop.JobId],
                    technician.Id,
                    sequence,
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
        }

        return new OptimizedDay(planned, solution.Unassigned, solution.Cost);
    }
}
