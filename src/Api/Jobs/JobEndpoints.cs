using MediatR;
using OpenDispatch.Api.Auth;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Jobs.AssignJob;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Jobs.GetJob;
using OpenDispatch.Application.Jobs.ListJobs;
using OpenDispatch.Contracts.Jobs;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Api.Jobs;

/// <summary>
/// Jobs: create, get, list, and the two ways a job moves — a status change and a manual
/// assignment (Document 3, step 47).
/// </summary>
/// <remarks>
/// <para>
/// Deliberately narrower than Customers/Technicians' CRUD, matching the build text exactly:
/// there is no <c>PUT /jobs/{id}</c> and no delete. A job's own fields do not get corrected once
/// booked — <c>ChangeJobStatusCommand</c> and <c>AssignJobCommand</c> are the only ways this step
/// gives a caller to change one, and both already exist (steps 35–36).
/// </para>
/// <para>
/// <see cref="Contracts.JobPriority"/>/<see cref="Contracts.JobStatus"/> convert to and from
/// their domain namesakes by a bare cast rather than a switch. That is only safe because
/// <c>ContractsAssemblyTests</c> pins the two enums name-for-name and number-for-number — the
/// cast is what that test exists to keep honest, not a shortcut around it.
/// </para>
/// </remarks>
public static class JobEndpoints
{
    public static IEndpointRouteBuilder MapJobEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var jobs = endpoints.MapGroup("/jobs")
            .RequireAuthorization(AuthPolicies.AdminOrDispatcher)
            .WithTags("Jobs");

        jobs.MapPost("/", CreateAsync).WithName("CreateJob")
            .Produces<CreateJobResponse>(StatusCodes.Status201Created);
        jobs.MapGet("/", ListAsync).WithName("ListJobs")
            .Produces<JobPageResponse>();
        jobs.MapGet("/{id:guid}", GetAsync).WithName("GetJob")
            .Produces<JobResponse>();
        jobs.MapPost("/{id:guid}/status", ChangeStatusAsync).WithName("ChangeJobStatus")
            .Produces(StatusCodes.Status204NoContent);
        jobs.MapPost("/{id:guid}/assign", AssignAsync).WithName("AssignJob")
            .Produces<AssignJobResponse>();

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateJobRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new CreateJobCommand(
            CustomerId.From(request.CustomerId),
            ServiceLocationId.From(request.LocationId),
            request.RequiredSkill,
            (Domain.Jobs.JobPriority)request.Priority,
            request.WindowStart,
            request.WindowEnd,
            request.EstimatedDuration);
        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(id => Results.Created($"/jobs/{id.Value}", new CreateJobResponse(id.Value)));
    }

    /// <remarks>Paged, with defaults — see <c>CustomerEndpoints.ListAsync</c>.</remarks>
    private static async Task<IResult> ListAsync(
        ISender sender,
        CancellationToken cancellationToken,
        int? page = null,
        int? pageSize = null)
    {
        var query = new ListJobsQuery(page ?? 1, pageSize ?? ListJobsQuery.DefaultPageSize);
        var result = await sender.Send(query, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(found => Results.Ok(new JobPageResponse(
            [.. found.Items.Select(ToResponse)],
            found.Total,
            found.Page,
            found.PageSize)));
    }

    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken cancellationToken)
    {
        var result = await sender.Send(new GetJobQuery(JobId.From(id)), cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(job => Results.Ok(ToResponse(job)));
    }

    private static async Task<IResult> ChangeStatusAsync(
        Guid id,
        ChangeJobStatusRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new ChangeJobStatusCommand(
            JobId.From(id), (Domain.Jobs.JobStatus)request.Status, request.CompletedAt);
        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult();
    }

    private static async Task<IResult> AssignAsync(
        Guid id,
        AssignJobRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new AssignJobCommand(JobId.From(id), TechnicianId.From(request.TechnicianId), request.ScheduledStart);
        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(assignmentId => Results.Ok(new AssignJobResponse(assignmentId.Value)));
    }

    private static JobResponse ToResponse(JobSummary job) => new(
        job.Id.Value,
        job.CustomerId.Value,
        job.LocationId.Value,
        job.Latitude,
        job.Longitude,
        job.RequiredSkill,
        (Contracts.JobPriority)job.Priority,
        job.WindowStart,
        job.WindowEnd,
        job.EstimatedDuration,
        (Contracts.JobStatus)job.Status,
        job.Notes);
}
