using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.Technicians;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.Application.Scheduling.InsertJob;

/// <summary>
/// Rebuilds the day as it stands, asks the engine where one more job fits, and writes only what
/// moved.
/// </summary>
/// <remarks>
/// <para>
/// The other half of dispatching, and the harder half to wire: <c>Solve</c> is handed a problem
/// and hands back a plan, but <c>Insert</c> is handed <em>the plan that already exists</em>, which
/// means the rows have to be turned back into one first. <see cref="CurrentDay"/> does that, and
/// what it cannot recover — when a technician arrived, as against when they started — is not
/// something the objective prices.
/// </para>
/// <para>
/// <strong>Only the delta is written.</strong> Every route the engine did not touch comes back
/// identical, and a stop asked to stay exactly where it is stays silent, so the writes and the
/// domain events are confined to the technician who took the job. That is not a diff computed here
/// — it falls out of asking each stop to go where the solution says it goes.
/// </para>
/// <para>
/// The horizon is not a parameter. It is the stretch of time the crew is working, widened to cover
/// the job's promised window: wide enough that every stop already planned for those technicians is
/// in the problem, which it has to be, because a stop the engine cannot see is one it will happily
/// plan an emergency on top of.
/// </para>
/// </remarks>
internal sealed class InsertJobHandler(
    IJobRepository jobs,
    ITechnicianRepository technicians,
    IAssignmentRepository assignments,
    IScheduler scheduler,
    ITenantContext tenant)
    : IRequestHandler<InsertJobCommand, Result<InsertedJob>>
{
    public async Task<Result<InsertedJob>> Handle(
        InsertJobCommand command,
        CancellationToken cancellationToken)
    {
        var arriving = await jobs.GetAsync(command.JobId, cancellationToken).ConfigureAwait(false);

        if (arriving is null)
        {
            return Result.Failure<InsertedJob>(JobErrors.NotFound(command.JobId));
        }

        if (!Job.SchedulableStatuses.Contains(arriving.Status))
        {
            return Result.Failure<InsertedJob>(JobErrors.NotSchedulable(arriving.Status));
        }

        // A job that already has a stop is not an insertion. Answering "done" would let a caller
        // who has lost track of the board carry on believing it; re-placing work is a drag or a
        // re-optimise, and both say so.
        if (await assignments.GetByJobAsync(arriving.Id, cancellationToken).ConfigureAwait(false) is not null)
        {
            return Result.Failure<InsertedJob>(SchedulingErrors.AlreadyPlanned(arriving.Id));
        }

        var crew = await technicians.ListAsync(cancellationToken).ConfigureAwait(false);
        var horizon = HorizonFor(arriving, crew);

        var planned = await assignments.ListInHorizonAsync(horizon, cancellationToken).ConfigureAwait(false);
        var known = await WorkBehindAsync(horizon, planned, arriving, cancellationToken).ConfigureAwait(false);

        var (routes, overlapping) = CurrentDay.Rebuild(planned, known);

        if (routes is null)
        {
            return Result.Failure<InsertedJob>(SchedulingErrors.OverlappingDay(overlapping!.Value));
        }

        var problem = new SchedulingProblem(
            horizon,
            crew.Select(ToPlan),
            known.Values.Select(ToSchedJob),
            Objective.Default,
            SchedulingDefaults.Seed);

        // The day's unplaced work is not this operation's business: it neither places it nor drops
        // it, and naming it here would only invite the answer to report on it. The cost is zero
        // for the same reason — the engine prices the day it returns and never reads this one.
        var today = new Solution(routes, [], 0d);
        var solved = scheduler.Insert(today, problem, arriving.Id);

        return solved.Unassigned.Contains(arriving.Id)
            ? Result.Failure<InsertedJob>(SchedulingErrors.CouldNotPlace(arriving.Id))
            : Result.Success(await ApplyAsync(problem, solved, known, arriving.Id, cancellationToken).ConfigureAwait(false));
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

    /// <summary>
    /// The stretch of time this insertion is about: everybody's working hours, widened to cover
    /// the window the customer was promised.
    /// </summary>
    /// <remarks>
    /// Derived rather than asked for, because the command carries only a job — and derived this
    /// way because the horizon's job is to make sure nothing already planned is invisible. A
    /// technician's shift is what their planning day is (step 9); the promised window is dragged in
    /// so that work booked for the edges of the day is still considered.
    /// </remarks>
    private static TimeWindow HorizonFor(Job arriving, IReadOnlyList<Technician> crew)
    {
        var horizon = arriving.Window;

        foreach (var technician in crew)
        {
            horizon = new TimeWindow(
                technician.Shift.Start < horizon.Start ? technician.Shift.Start : horizon.Start,
                technician.Shift.End > horizon.End ? technician.Shift.End : horizon.End);
        }

        return horizon;
    }

    /// <summary>
    /// Every job the problem has to know about: the schedulable work in the horizon, the job
    /// arriving, and the work behind any stop already planned.
    /// </summary>
    /// <remarks>
    /// That last group is the one worth spelling out. A technician already <em>en route</em> to a
    /// job is not schedulable work, so it is not in the horizon's list — but their van is still
    /// occupied by it, and an engine that could not see the stop would cheerfully plan an
    /// emergency over the top. So a stop whose job the list does not cover is fetched by id.
    /// </remarks>
    private async Task<Dictionary<JobId, Job>> WorkBehindAsync(
        TimeWindow horizon,
        IReadOnlyList<Assignment> planned,
        Job arriving,
        CancellationToken cancellationToken)
    {
        var schedulable = await jobs.ListSchedulableAsync(horizon, cancellationToken).ConfigureAwait(false);
        var known = schedulable.ToDictionary(job => job.Id);
        known[arriving.Id] = arriving;

        foreach (var stop in planned)
        {
            if (known.ContainsKey(stop.JobId))
            {
                continue;
            }

            if (await jobs.GetAsync(stop.JobId, cancellationToken).ConfigureAwait(false) is { } behind)
            {
                known[behind.Id] = behind;
            }
        }

        return known;
    }

    /// <summary>
    /// Writes the plan the engine returned, which for every route but one is what was there
    /// already.
    /// </summary>
    private async Task<InsertedJob> ApplyAsync(
        SchedulingProblem problem,
        Solution solved,
        Dictionary<JobId, Job> known,
        JobId arriving,
        CancellationToken cancellationToken)
    {
        Assignment? inserted = null;
        var host = default(TechnicianId);
        var displaced = 0;

        foreach (var technician in problem.Technicians)
        {
            var route = solved.RouteFor(technician.Id);

            for (var sequence = 0; sequence < route.Length; sequence++)
            {
                var stop = route[sequence];

                var assignment = await StopPlacement.PlaceAsync(
                    assignments,
                    tenant.OrgId,
                    known[stop.JobId],
                    technician.Id,
                    sequence,
                    stop.Start,
                    stop.TravelMin,
                    cancellationToken).ConfigureAwait(false);

                if (stop.JobId != arriving)
                {
                    continue;
                }

                inserted = assignment;
                host = technician.Id;

                // Everything after it on that technician's day is what the emergency cost
                // somebody else's afternoon.
                displaced = route.Length - sequence - 1;
            }
        }

        return new InsertedJob(
            inserted!.Id,
            host,
            inserted.ScheduledStart,
            inserted.Sequence,
            displaced);
    }
}
