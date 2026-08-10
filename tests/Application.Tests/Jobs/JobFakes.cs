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

    /// <summary>
    /// Not implemented, deliberately.
    /// </summary>
    /// <remarks>
    /// Nothing in this slice plans a day — step 37 is the first thing to read the schedulable
    /// work, and it will want a fake that answers the way the real query does, over a horizon it
    /// controls. Guessing at that now would be an implementation nothing exercises and everything
    /// would then trust.
    /// </remarks>
    public Task<IReadOnlyList<Job>> ListSchedulableAsync(TimeWindow horizon, CancellationToken ct) =>
        throw new NotSupportedException(
            "No request before step 37 reads the schedulable work; write this fake with the slice that needs it.");
}
