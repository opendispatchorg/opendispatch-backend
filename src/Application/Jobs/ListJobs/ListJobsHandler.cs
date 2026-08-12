using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Application.Jobs.ListJobs;

/// <summary>Reads the tenant's jobs and summarises them.</summary>
/// <remarks>
/// The order is the repository's, by promised window, and is not restated here — the same
/// reasoning <c>ListCustomersHandler</c> gives for leaving sorting to the query.
/// </remarks>
internal sealed class ListJobsHandler(IJobRepository jobs)
    : IRequestHandler<ListJobsQuery, Result<IReadOnlyList<JobSummary>>>
{
    public async Task<Result<IReadOnlyList<JobSummary>>> Handle(
        ListJobsQuery query,
        CancellationToken cancellationToken)
    {
        var found = await jobs.ListAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<JobSummary> summaries = [.. found.Select(Project)];

        return Result.Success(summaries);
    }

    private static JobSummary Project(Job job) => new(
        job.Id,
        job.CustomerId,
        job.LocationId,
        job.Location.Lat,
        job.Location.Lng,
        job.RequiredSkill,
        job.Priority,
        job.Window.Start,
        job.Window.End,
        job.EstimatedDuration,
        job.Status,
        job.Notes);
}
