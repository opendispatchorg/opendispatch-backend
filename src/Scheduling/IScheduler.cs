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
}
