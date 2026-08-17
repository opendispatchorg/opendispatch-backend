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

    /// <remarks>
    /// By promised window, because this is read by a dispatcher scanning what is coming up —
    /// unlike <see cref="ListSchedulableAsync"/>, which orders for the scheduler's own reasons. The
    /// id breaks ties so a page boundary cannot fall between two jobs promised for the same instant
    /// and show one of them twice.
    /// </remarks>
    public async Task<Page<Job>> ListAsync(PageRequest page, CancellationToken ct)
    {
        var total = await context.Jobs.CountAsync(ct).ConfigureAwait(false);

        var items = await context.Jobs
            .OrderBy(job => job.Window.Start)
            .ThenBy(job => job.Id)
            .Skip(page.Skip)
            .Take(page.Size)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new Page<Job>(items, total);
    }

    /// <remarks>
    /// Oldest first, so an operator reading an erasure's audit trail sees the customer's history in
    /// the order it happened.
    /// </remarks>
    public async Task<IReadOnlyList<Job>> ListForCustomerAsync(CustomerId customer, CancellationToken ct) =>
        await context.Jobs
            .Where(job => job.CustomerId == customer)
            .OrderBy(job => job.Window.Start)
            .ThenBy(job => job.Id)
            .ToListAsync(ct);

    /// <remarks>
    /// Streamed for the export — see <c>CustomerRepository.StreamAsync</c>.
    /// <para>
    /// <strong>No-tracking, and that is not an optimisation.</strong> A tracked stream puts every row
    /// it hands out into the change tracker and holds it there until the request ends — so an export
    /// that streams precisely so a shop's history need not be held in memory would hold all of it
    /// anyway, one identity map at a time. Measured on a year of history: the peak came down by
    /// roughly a third. Nothing saves a projection, so there is nothing to track for.
    /// </para>
    /// </remarks>
    public IAsyncEnumerable<Job> StreamAsync(CancellationToken ct) =>
        context.Jobs
            .AsNoTracking()
            .OrderBy(job => job.Window.Start)
            .ThenBy(job => job.Id)
            .AsAsyncEnumerable();

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
