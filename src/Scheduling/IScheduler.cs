using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.Scheduling;

/// <summary>
/// Turns a scheduling problem into a plan.
/// </summary>
/// <remarks>
/// The whole engine behind one method, so the layer above depends on the idea of scheduling
/// rather than on how it is done. Swapping a better search in later is a registration change
/// and nothing else.
/// </remarks>
public interface IScheduler
{
    /// <summary>
    /// Plans the whole horizon: every job placed on a technician, or reported as one nobody
    /// could take.
    /// </summary>
    /// <remarks>
    /// Never throws to say a job is impossible — an unschedulable job comes back in
    /// <see cref="Solution.Unassigned"/>, because a dispatcher needs to see it and decide,
    /// not read a stack trace.
    /// </remarks>
    Solution Solve(SchedulingProblem problem);

    /// <summary>
    /// Finds the best place in a day that is already planned for one more job, leaving every
    /// other stop where it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The other half of dispatching, and a different question from <see cref="Solve"/>. A
    /// boiler has failed at eleven o'clock; the dispatcher wants to know where it fits, not to
    /// be handed a new plan for an afternoon half of which has already been driven.
    /// </para>
    /// <para>
    /// Every scheduled stop keeps its technician and its place in their run. Only the clock
    /// moves, for the stops after the one slotted in.
    /// </para>
    /// </remarks>
    /// <param name="current">The day as it stands.</param>
    /// <param name="problem">The problem it answers — the source of the job, the shifts and the prices.</param>
    /// <param name="job">What has just come in. Must be in the problem and not already scheduled.</param>
    /// <returns>
    /// The day with the job in the best place it will go, or with the job reported unassigned
    /// if it will not go anywhere.
    /// </returns>
    Solution Insert(Solution current, SchedulingProblem problem, JobId job);

    /// <summary>
    /// Re-times one technician's run, in the order it is already in, so it is a day somebody could
    /// actually drive.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The third question, and the one that has no plan in it. <see cref="Solve"/> decides who does
    /// what; <see cref="Insert"/> decides where one more job goes; this decides <em>nothing</em>. It
    /// takes the sequence exactly as given — a dispatcher's own order, dragged by hand — and works
    /// out the clock: the drives between the stops, the waiting before a window opens, and whether
    /// the whole thing still finishes inside the shift.
    /// </para>
    /// <para>
    /// It exists because the manual path can produce a day that is not a route. Dragging two stops
    /// onto the same hour is an instruction this system carries out faithfully and does not re-time
    /// around, so the day can end up overlapping itself — at which point an emergency insertion has
    /// nothing to insert into and refuses. This is the way back that does not throw away the
    /// dispatcher's decisions.
    /// </para>
    /// <para>
    /// Nothing is priced and nothing is compared: the answer is the same run, timed, or nothing at
    /// all if that technician cannot drive it — a stop they are not qualified for, or a day that
    /// would run past the end of their shift.
    /// </para>
    /// </remarks>
    /// <param name="problem">The day: this technician, the work, and the horizon it happens in.</param>
    /// <param name="technician">Whose run to time. Must be in the problem.</param>
    /// <param name="order">The jobs, in the order they will be driven. Must all be in the problem.</param>
    /// <param name="notBefore">
    /// The times these stops were already promised for, so re-timing can push a stop later but
    /// never pull one earlier than the customer was told.
    /// </param>
    /// <returns>The timed run, or <see langword="null"/> if it cannot be driven.</returns>
    ImmutableArray<Stop>? Retime(
        SchedulingProblem problem,
        TechnicianId technician,
        IReadOnlyList<JobId> order,
        IReadOnlyDictionary<JobId, DateTimeOffset> notBefore);
}
