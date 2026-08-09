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
}
