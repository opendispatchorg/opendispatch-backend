using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Tests.Jobs;

/// <summary>
/// <see cref="IJobRepository"/> over a <see cref="FakeStore{TAggregate}"/>, scoped to a tenant
/// like the real one.
/// </summary>
internal sealed class FakeJobRepository(FakeStore<Job> store, ITenantContext tenant) : IJobRepository
{
    public Task<Job?> GetAsync(JobId id, CancellationToken ct) =>
        Task.FromResult(store.Owned(tenant.OrgId).FirstOrDefault(job => job.Id == id));

    public void Add(Job job) => store.Stage(job);

    /// <remarks>The real query's order, skip, take and count, restated in memory.</remarks>
    public Task<Page<Job>> ListAsync(PageRequest page, CancellationToken ct)
    {
        var all = store.Owned(tenant.OrgId)
            .OrderBy(job => job.Window.Start)
            .ThenBy(job => job.Id.Value)
            .ToList();

        return Task.FromResult(new Page<Job>([.. all.Skip(page.Skip).Take(page.Size)], all.Count));
    }

    /// <remarks>The real query's filter and order, restated in memory.</remarks>
    public Task<IReadOnlyList<Job>> ListForCustomerAsync(CustomerId customer, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Job>>(
        [
            .. store.Owned(tenant.OrgId)
                .Where(job => job.CustomerId == customer)
                .OrderBy(job => job.Window.Start)
                .ThenBy(job => job.Id.Value),
        ]);

    public async IAsyncEnumerable<Job> StreamAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var job in store.Owned(tenant.OrgId)
            .OrderBy(job => job.Window.Start)
            .ThenBy(job => job.Id.Value))
        {
            yield return job;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <remarks>
    /// <para>
    /// Both halves of the real query, restated in memory: the domain's own
    /// <c>SchedulableStatuses</c> rather than a status list of its own, and the same half-open
    /// overlap against the horizon — a job whose window closes exactly as the horizon opens is out.
    /// </para>
    /// <para>
    /// Ordered like the real one, because the optimiser consumes it in the order it arrives and
    /// Document 2 §4 requires the same problem to produce the same plan.
    /// </para>
    /// </remarks>
    public Task<IReadOnlyList<Job>> ListSchedulableAsync(TimeWindow horizon, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Job>>(
        [
            .. store.Owned(tenant.OrgId)
                .Where(job => Job.SchedulableStatuses.Contains(job.Status))
                .Where(job => job.Window.Start < horizon.End && horizon.Start < job.Window.End)
                .OrderBy(job => job.Window.Start)
                .ThenBy(job => job.Id.Value),
        ]);
}
