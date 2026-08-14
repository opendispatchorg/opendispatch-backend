using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Scheduling.OptimizeDay;

/// <summary>
/// The part of a technician's day that is already spoken for: when it frees them, and where the
/// numbering of their run has got to.
/// </summary>
/// <param name="EndsAt">When the last stop the optimiser may not touch finishes.</param>
/// <param name="NextSequence">
/// The first position in the run that is still free — one past the highest a committed stop holds,
/// so a re-planned day cannot number two stops the same.
/// </param>
internal readonly record struct CommittedDay(DateTimeOffset EndsAt, int NextSequence);

/// <summary>
/// Works out, per technician, how much of the day the optimiser has already lost.
/// </summary>
/// <remarks>
/// <para>
/// A re-plan only moves work that is still schedulable (<see cref="Job.SchedulableStatuses"/>),
/// which is right — a job somebody is driving to belongs to them. But the <em>stop</em> for that
/// job stays in the plan, and the engine is never told about it: it is handed the crew's shifts and
/// plans from the start of them, so a day re-planned at two o'clock puts a fresh stop on top of the
/// one the technician is currently at. The symptom is a technician double-booked on the board, two
/// stops sharing a position in the run, and every later emergency insertion refused, because a day
/// that overlaps itself is not a route anything can be slotted into.
/// </para>
/// <para>
/// So committed work is subtracted from what the engine is offered rather than described to it:
/// the technician's shift begins again when their committed work ends. It is the smallest thing
/// that is true — the engine's model has no notion of a stop that cannot move, and inventing one
/// would mean teaching the constructor, every local-search move and the objective about it.
/// </para>
/// <para>
/// What that costs, stated rather than discovered: the first re-planned stop's drive is measured
/// from the technician's home base, not from the site they are actually standing on, so its travel
/// figure is an estimate rather than the truth. It costs accuracy in the objective and in one
/// number on the board; it cannot produce a plan that overlaps itself.
/// <em>Flip it if:</em> a <c>StartFrom</c> point on the engine's <c>TechPlan</c> ever
/// earns its place — that is the honest fix, and it is a change to the engine's model rather than
/// to a handler.
/// </para>
/// </remarks>
internal static class CommittedWork
{
    /// <summary>Nobody has anything already under way.</summary>
    public static IReadOnlyDictionary<TechnicianId, CommittedDay> None { get; } =
        new Dictionary<TechnicianId, CommittedDay>();

    /// <summary>
    /// Reads the committed part of each technician's day off the stops the optimiser is not
    /// allowed to move.
    /// </summary>
    /// <param name="stops">Stops whose job this re-plan does not cover.</param>
    /// <param name="jobs">The work behind those stops, by id — the source of how long each takes.</param>
    public static IReadOnlyDictionary<TechnicianId, CommittedDay> From(
        IEnumerable<Assignment> stops,
        IReadOnlyDictionary<JobId, Job> jobs)
    {
        var committed = new Dictionary<TechnicianId, CommittedDay>();

        foreach (var stop in stops)
        {
            // A stop whose job cannot be read is one that outlived its job, which nothing in the
            // system does. Counted for its position but not for time, because guessing a duration
            // would be inventing capacity or destroying it.
            var ends = jobs.TryGetValue(stop.JobId, out var job)
                ? stop.ScheduledStart + job.EstimatedDuration
                : stop.ScheduledStart;

            var day = committed.GetValueOrDefault(
                stop.TechnicianId,
                new CommittedDay(DateTimeOffset.MinValue, 0));

            committed[stop.TechnicianId] = new CommittedDay(
                ends > day.EndsAt ? ends : day.EndsAt,
                stop.Sequence + 1 > day.NextSequence ? stop.Sequence + 1 : day.NextSequence);
        }

        return committed;
    }

    /// <summary>
    /// The hours of <paramref name="shift"/> the optimiser may still plan into.
    /// </summary>
    /// <remarks>
    /// A technician whose committed work runs past the end of their shift is left with an empty
    /// window rather than an inverted one: no stop can finish inside nothing, so the engine gives
    /// them no work, which is exactly the answer.
    /// </remarks>
    public static TimeWindow Remaining(TimeWindow shift, CommittedDay committed)
    {
        if (committed.EndsAt <= shift.Start)
        {
            return shift;
        }

        return new TimeWindow(committed.EndsAt < shift.End ? committed.EndsAt : shift.End, shift.End);
    }
}
