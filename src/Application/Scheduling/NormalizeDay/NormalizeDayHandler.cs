using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Application.Technicians;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.Technicians;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.Application.Scheduling.NormalizeDay;

/// <summary>
/// Reads one technician's day, asks the engine to time it, and writes the clock back.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately the dullest handler in the scheduling folder. It decides nothing: not who does the
/// work, not the order, not which jobs are in the day. The order comes off the clock the stops
/// already carry — <c>CurrentDay</c>'s rule, because a hand-dragged day leaves the stored sequence
/// behind — and the timing comes from the same <c>RouteTimer</c> every other path is judged by.
/// </para>
/// <para>
/// <strong>What it refuses.</strong> A run that cannot be driven at all — a stop the technician is
/// not qualified for, or a day that would now finish after their shift — is reported rather than
/// half-applied. Making room by dropping somebody's work is a decision for a dispatcher, and the
/// answer they need is "this day does not fit", not a plan that quietly lost a job.
/// </para>
/// </remarks>
internal sealed class NormalizeDayHandler(
    IAssignmentRepository assignments,
    IJobRepository jobs,
    ITechnicianRepository technicians,
    IScheduler scheduler)
    : IRequestHandler<NormalizeDayCommand, Result<NormalizedDay>>
{
    public async Task<Result<NormalizedDay>> Handle(
        NormalizeDayCommand command,
        CancellationToken cancellationToken)
    {
        var technician = await technicians.GetAsync(command.TechnicianId, cancellationToken).ConfigureAwait(false);

        if (technician is null)
        {
            return Result.Failure<NormalizedDay>(TechnicianErrors.NotFound(command.TechnicianId));
        }

        var horizon = new TimeWindow(command.From, command.To);
        var planned = await assignments.ListInHorizonAsync(horizon, cancellationToken).ConfigureAwait(false);

        // By the clock, not by the stored sequence: the order a run is driven in is the order of
        // the day, and a dragged day's numbering is exactly what has stopped being true.
        var day = planned
            .Where(stop => stop.TechnicianId == command.TechnicianId)
            .OrderBy(stop => stop.ScheduledStart)
            .ThenBy(stop => stop.Id.Value)
            .ToList();

        if (day.Count == 0)
        {
            // Nothing to repair is a success. An empty day is a drivable day, and a caller
            // repairing a technician who is not working today should not have to special-case it.
            return Result.Success(new NormalizedDay(0, 0, null, null));
        }

        var work = await WorkBehindAsync(day, cancellationToken).ConfigureAwait(false);

        if (work is null)
        {
            return Result.Failure<NormalizedDay>(SchedulingErrors.CannotNormalize(command.TechnicianId));
        }

        var problem = new SchedulingProblem(
            horizon,
            [ToPlan(technician)],
            work.Values.Select(ToSchedJob),
            Objective.Default,
            SchedulingDefaults.Seed);

        // The promises already made: every stop may be pushed later to make room, and none may be
        // pulled earlier than the customer was told.
        var promised = day.ToDictionary(stop => stop.JobId, stop => stop.ScheduledStart);
        var order = day.Select(stop => stop.JobId).ToList();

        var timed = scheduler.Retime(problem, technician.Id, order, promised);

        if (timed is not { } run)
        {
            return Result.Failure<NormalizedDay>(SchedulingErrors.UndrivableDay(command.TechnicianId));
        }

        var byJob = day.ToDictionary(stop => stop.JobId);
        var moved = 0;

        for (var sequence = 0; sequence < run.Length; sequence++)
        {
            var stop = run[sequence];
            var assignment = byJob[stop.JobId];

            // Counted on the clock alone. A stop whose sequence number is corrected but whose time
            // is unchanged has not moved in any sense a dispatcher cares about — the numbering is
            // bookkeeping the drag left behind, and reporting it as a move would make the answer to
            // "how much of my afternoon just changed" wrong in the alarming direction.
            if (assignment.ScheduledStart != stop.Start)
            {
                moved++;
            }

            // Through the aggregate, so the board hears about it: Reschedule raises
            // AssignmentChanged, which is what repaints a dispatcher's screen and what a
            // technician's next pull reads.
            assignment.Reschedule(stop.Start, sequence, stop.TravelMin);
        }

        return Result.Success(new NormalizedDay(run.Length, moved, run[0].Start, run[^1].End));
    }

    /// <summary>
    /// The jobs behind this technician's stops, or <see langword="null"/> if one of them is gone.
    /// </summary>
    /// <remarks>
    /// Fetched by id rather than through the schedulable list, because that is not the question
    /// here: a stop for a job already under way is still on the technician's day and still takes up
    /// the hours it takes up. A stop whose job this tenant cannot see is a plan that has come apart
    /// in a way this operation must not paper over.
    /// </remarks>
    private async Task<Dictionary<JobId, Job>?> WorkBehindAsync(
        List<Assignment> day,
        CancellationToken cancellationToken)
    {
        var work = new Dictionary<JobId, Job>(day.Count);

        foreach (var stop in day)
        {
            if (await jobs.GetAsync(stop.JobId, cancellationToken).ConfigureAwait(false) is not { } job)
            {
                return null;
            }

            work[job.Id] = job;
        }

        return work;
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
}
