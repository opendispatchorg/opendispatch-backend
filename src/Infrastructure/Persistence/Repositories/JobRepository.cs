using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IJobRepository"/>
internal sealed class JobRepository(AppDbContext context) : IJobRepository
{
    public Task<Job?> GetAsync(JobId id, CancellationToken ct) =>
        context.Jobs.FirstOrDefaultAsync(job => job.Id == id, ct);

    public void Add(Job job) => context.Jobs.Add(job);

    public async Task<IReadOnlyList<Job>> ListSchedulableAsync(TimeWindow horizon, CancellationToken ct) =>
        await context.Jobs
            // The statuses come from the domain, not from a list written here. Three callers ask
            // this question and a fourth is coming; if the answer lived in this Where clause they
            // could each end up with a different one.
            .Where(job => Job.SchedulableStatuses.Contains(job.Status))

            // Half-open, matching TimeWindow.Overlaps: a job whose window closes exactly as the
            // horizon opens is not in it. Written out rather than called because the domain
            // method cannot cross into SQL — the two definitions are pinned together by test.
            .Where(job => job.Window.Start < horizon.End && horizon.Start < job.Window.End)

            // Ordered because the scheduler is required to be deterministic under a seed
            // (Document 2 §4), and greedy insertion consumes this list in order — an unordered
            // read would make the same problem produce different plans on different runs. The id
            // breaks ties so the order is total.
            .OrderBy(job => job.Window.Start)
            .ThenBy(job => job.Id)
            .ToListAsync(ct);
}
