using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Loads and stores assignments — the plan.
/// </summary>
/// <remarks>
/// <para>
/// The widest of the persistence ports, because the plan is the thing that churns: a
/// dispatcher drags a stop, the optimiser rewrites a day, an emergency is slotted in. Jobs
/// are booked and worked; assignments are made, moved, and thrown away.
/// </para>
/// <para>
/// It never returns a <c>Job</c> alongside an assignment, and the aggregates make that
/// impossible anyway — an assignment holds a <see cref="JobId"/> and no navigation property.
/// A caller that needs both asks both repositories.
/// </para>
/// </remarks>
public interface IAssignmentRepository
{
    /// <summary>Fetches one stop by its own identity.</summary>
    /// <returns>The assignment, or <see langword="null"/> if this tenant has no such assignment.</returns>
    Task<Assignment?> GetAsync(AssignmentId id, CancellationToken ct);

    /// <summary>Stages a newly planned stop.</summary>
    void Add(Assignment assignment);

    /// <summary>
    /// Drops a stop out of the plan.
    /// </summary>
    /// <remarks>
    /// The only aggregate in the system that is genuinely deleted. A job is cancelled and a
    /// customer is kept forever, but re-optimising a day that can no longer fit a job must
    /// leave no stop behind for it — an orphaned assignment would show on the board as work
    /// somebody is expected to drive to.
    /// </remarks>
    void Remove(Assignment assignment);

    /// <summary>
    /// Finds the stop planned for a job, if it has one.
    /// </summary>
    /// <remarks>
    /// Returning at most one asserts a rule the aggregates cannot express by themselves: a
    /// job is planned once, so assigning an already-assigned job moves its existing stop
    /// rather than adding a second one. The persistence layer should back that with a unique
    /// index, not leave it to the handlers to maintain.
    /// </remarks>
    Task<Assignment?> GetByJobAsync(JobId jobId, CancellationToken ct);

    /// <summary>
    /// Fetches the plan as it currently stands over a horizon, by scheduled start.
    /// </summary>
    /// <remarks>
    /// Both scheduling paths need it: re-optimising a day has to rewrite the stops already
    /// on it, and inserting an emergency has to rebuild the day it is being inserted into.
    /// The <c>horizon</c> is the stretch of time being planned.
    /// </remarks>
    Task<IReadOnlyList<Assignment>> ListInHorizonAsync(TimeWindow horizon, CancellationToken ct);

    /// <summary>
    /// Fetches every assignment in the tenant, unbounded — step 49's <c>GET /export</c>, which
    /// wants the whole plan rather than a horizon's worth of it. Unpaged, following
    /// <see cref="ICustomerRepository.ListAsync"/>: business data a shop is entitled to take with
    /// it, not a display list a page has to render.
    /// </summary>
    IAsyncEnumerable<Assignment> StreamAsync(CancellationToken ct);
}
