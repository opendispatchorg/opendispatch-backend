using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Application.Technicians;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.Technicians;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling.Travel;

namespace OpenDispatch.Application.Jobs.AssignJob;

/// <summary>
/// Puts a stop on a technician's day: creating one, moving one, or handing one to somebody else.
/// </summary>
/// <remarks>
/// <para>
/// Three aggregates in one transaction, and the split between them is the point. The
/// <c>Assignment</c> is the plan and takes all the churn; the <c>Job</c> is the demand and only
/// learns that it is now planned; the <c>Technician</c> is read and never written. Re-planning a
/// day therefore rewrites stops and leaves the work alone, which is the whole reason those are
/// separate aggregates.
/// </para>
/// <para>
/// <strong>A job has at most one stop</strong>, which is <see cref="StopPlacement"/>'s rule rather
/// than this handler's — the optimiser keeps the same one. What is this handler's is deciding
/// where the stop goes: a dispatcher is telling the system, so it is told.
/// </para>
/// <para>
/// <strong>What it does not do is re-time the rest of the day.</strong> A stop dropped into the
/// middle of a run leaves the stops after it with the sequence and travel they already had, which
/// are now measured from a stop that is no longer their predecessor. Fixing that properly is
/// re-timing a route, which is <c>RouteTimer</c>'s job in the engine and step 37's job in the
/// application — doing it here would be a second, hand-written copy of the rule that decides what
/// a feasible day looks like.
/// </para>
/// </remarks>
internal sealed class AssignJobHandler(
    IJobRepository jobs,
    ITechnicianRepository technicians,
    IAssignmentRepository assignments,
    ITravelTimeProvider travel,
    ITenantContext tenant)
    : IRequestHandler<AssignJobCommand, Result<AssignmentId>>
{
    public async Task<Result<AssignmentId>> Handle(
        AssignJobCommand command,
        CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(command.JobId, cancellationToken).ConfigureAwait(false);

        if (job is null)
        {
            return Result.Failure<AssignmentId>(JobErrors.NotFound(command.JobId));
        }

        var technician = await technicians
            .GetAsync(command.TechnicianId, cancellationToken)
            .ConfigureAwait(false);

        if (technician is null)
        {
            return Result.Failure<AssignmentId>(TechnicianErrors.NotFound(command.TechnicianId));
        }

        // The domain's own answer to "may this work still be moved", so the board, the optimiser
        // and this path cannot hold three opinions. Work that is under way belongs to the
        // technician doing it.
        if (!Job.SchedulableStatuses.Contains(job.Status))
        {
            return Result.Failure<AssignmentId>(JobErrors.NotSchedulable(job.Status));
        }

        // UTC before it reaches the domain, as everywhere else an instant arrives from outside.
        var start = command.ScheduledStart;
        var run = await DayOfAsync(technician, job.Id, cancellationToken).ConfigureAwait(false);
        var placement = await PlaceAsync(technician, job, run, start, cancellationToken).ConfigureAwait(false);

        // The one-stop-per-job rule is kept in one place, because the optimiser keeps it too.
        var assignment = await StopPlacement.PlaceAsync(
            assignments,
            tenant.OrgId,
            job,
            technician.Id,
            placement.Sequence,
            start,
            placement.TravelMin,
            cancellationToken).ConfigureAwait(false);

        return Result.Success(assignment.Id);
    }

    /// <summary>
    /// The technician's other stops over their shift, in the order they are driven.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The shift is the horizon because a shift is what a technician's planning day <em>is</em>
    /// (step 9) — and because the alternative, a calendar day, needs a timezone that nothing in
    /// the system models. A stop placed outside the shift by hand is still found by everything
    /// after it; a stop placed outside it <em>before</em> this one is not, and would be missed as
    /// a predecessor.
    /// </para>
    /// <para>
    /// The job's own stop is left out. Moving a job later in its own day must not measure the
    /// drive from where it already was.
    /// </para>
    /// </remarks>
    private async Task<IReadOnlyList<Assignment>> DayOfAsync(
        Technician technician,
        JobId moving,
        CancellationToken cancellationToken)
    {
        var horizon = await assignments
            .ListInHorizonAsync(technician.Shift, cancellationToken)
            .ConfigureAwait(false);

        return
        [
            .. horizon.Where(stop => stop.TechnicianId == technician.Id && stop.JobId != moving),
        ];
    }

    /// <summary>
    /// Where the stop falls in the run, and how far it is from whatever comes before it.
    /// </summary>
    /// <remarks>
    /// The first stop of a day is measured from the technician's home base, and every other from
    /// the stop before it — the same two rules the engine's <c>RouteTimer</c> uses, so a stop
    /// placed by hand and the same stop placed by the optimiser report the same drive.
    /// </remarks>
    private async Task<(int Sequence, double TravelMin)> PlaceAsync(
        Technician technician,
        Job job,
        IReadOnlyList<Assignment> run,
        DateTimeOffset start,
        CancellationToken cancellationToken)
    {
        var before = run.Where(stop => stop.ScheduledStart <= start).ToList();
        var previous = before.Count == 0 ? null : before[^1];

        var from = previous is null
            ? technician.HomeBase
            : await LocationOfAsync(previous.JobId, cancellationToken).ConfigureAwait(false)
                ?? technician.HomeBase;

        return (before.Count, travel.Minutes(from, job.Location));
    }

    /// <summary>
    /// Where the work on a planned stop happens.
    /// </summary>
    /// <returns>
    /// The point, or <see langword="null"/> if the job behind the stop cannot be read — which
    /// means a stop outlived its job, something nothing in the system does. The caller measures
    /// from the home base instead rather than failing a dispatcher's drag over it.
    /// </returns>
    private async Task<GeoPoint?> LocationOfAsync(JobId jobId, CancellationToken cancellationToken)
    {
        var previous = await jobs.GetAsync(jobId, cancellationToken).ConfigureAwait(false);

        return previous?.Location;
    }
}
