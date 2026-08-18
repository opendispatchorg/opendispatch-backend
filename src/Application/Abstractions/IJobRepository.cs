using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Loads and stores jobs — the demand side of the system.
/// </summary>
/// <remarks>
/// <para>
/// Five methods, and each one is here because a named step cannot be written without it —
/// <see cref="ListAsync"/> is step 47's, added when <c>GET /jobs</c> needed something
/// <see cref="ListSchedulableAsync"/> could not answer (it is scoped to a horizon and to the
/// statuses still worth planning; a reader wants everything). There is no <c>Update</c>, no
/// <c>Save</c>, and no query that is merely plausible: the point of a port is to say exactly
/// what the application needs, so that the day someone needs something else, the need is
/// visible in a diff.
/// </para>
/// <para>
/// Nothing here takes an <c>OrgId</c>. Tenant scope is ambient — the persistence layer
/// applies it to every query through a global filter (step 29), which is what makes
/// cross-tenant leakage structurally impossible rather than something each handler has to
/// remember.
/// </para>
/// </remarks>
public interface IJobRepository
{
    /// <summary>
    /// Fetches one job for changing: the status transitions, the assign path, invoicing, and
    /// every status op that arrives from a phone.
    /// </summary>
    /// <returns>The job, or <see langword="null"/> if this tenant has no such job.</returns>
    Task<Job?> GetAsync(JobId id, CancellationToken ct);

    /// <summary>
    /// Stages a newly booked job. It is written when the unit of work is saved, not here.
    /// </summary>
    void Add(Job job);

    /// <summary>
    /// Fetches one page of the tenant's jobs, for a reader rather than the scheduler — step 47's
    /// <c>GET /jobs</c> — and how many there are altogether.
    /// </summary>
    /// <remarks>
    /// Paged for the reason <c>ICustomerRepository.ListAsync</c> is, and more urgently: a job list
    /// grows every working day of the business's life, so the unpaged version was the query most
    /// certain to eventually hand a dispatcher a year of history to draw one screen.
    /// </remarks>
    Task<Page<Job>> ListAsync(PageRequest page, CancellationToken ct);

    /// <summary>
    /// Every job in the tenant, one at a time. For the export, and only for the export — see
    /// <c>ICustomerRepository.StreamAsync</c>.
    /// </summary>
    IAsyncEnumerable<Job> StreamAsync(CancellationToken ct);

    /// <summary>
    /// Fetches every job ever booked for one customer, for changing.
    /// </summary>
    /// <remarks>
    /// For erasure, and unpaged because of what it is for: a job carries its own copy of the
    /// customer's coordinates and whatever was written about the visit, so erasing a person means
    /// reaching all of their jobs rather than a page of them. It is bounded by one customer's
    /// history with one shop, and it is a rare, deliberate, admin-only act.
    /// </remarks>
    Task<IReadOnlyList<Job>> ListForCustomerAsync(CustomerId customer, CancellationToken ct);

    /// <summary>
    /// Fetches the work the scheduler is allowed to plan over a horizon — everything the
    /// optimiser needs to build a problem, and everything the emergency-insert path needs to
    /// reconstruct the day as it stands.
    /// </summary>
    /// <remarks>
    /// <para>
    /// "Schedulable" is a domain question — which statuses can still be planned into
    /// somebody's day, and which are finished, abandoned, or already under way. The
    /// implementation must not answer it with a hand-written status list of its own; when
    /// this is implemented the predicate belongs on the domain side of the boundary, so the
    /// board, the optimiser and the sync endpoint cannot each hold a different opinion.
    /// </para>
    /// <para>
    /// Returning aggregates rather than a projection is correct here and unusual: the
    /// optimiser genuinely consumes a job's window, skill, priority, duration and location.
    /// Read paths that only display jobs go through a read model instead.
    /// </para>
    /// <para>
    /// The <c>horizon</c> is the stretch of time being planned, and jobs are selected
    /// against it by their promised window.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<Job>> ListSchedulableAsync(TimeWindow horizon, CancellationToken ct);
}
