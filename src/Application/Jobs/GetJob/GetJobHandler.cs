using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Application.Jobs.GetJob;

/// <summary>Loads the job and projects it.</summary>
internal sealed class GetJobHandler(IJobRepository jobs) : IRequestHandler<GetJobQuery, Result<JobSummary>>
{
    public async Task<Result<JobSummary>> Handle(GetJobQuery query, CancellationToken cancellationToken)
    {
        var job = await jobs.GetAsync(query.Id, cancellationToken).ConfigureAwait(false);

        return job is null
            ? Result.Failure<JobSummary>(JobErrors.NotFound(query.Id))
            : Result.Success(Project(job));
    }

    // Mirrors ListJobsHandler.Project. Not shared, for the same reason GetTechnicianHandler's
    // is not: two short call sites is cheaper to keep than to abstract.
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
        job.Notes,
        job.ErasedAt);
}
