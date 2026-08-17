using MediatR;
using OpenDispatch.Api.Auth;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Api.Security;
using OpenDispatch.Application.Scheduling.InsertJob;
using OpenDispatch.Application.Scheduling.NormalizeDay;
using OpenDispatch.Application.Scheduling.OptimizeDay;
using OpenDispatch.Contracts.Schedule;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Api.Schedule;

/// <summary>
/// The two ways a day gets planned — a full re-plan and a single emergency insertion (Document 3,
/// step 48).
/// </summary>
/// <remarks>
/// <c>AdminOrDispatcher</c>, the same policy Customers and Jobs use (step 47): planning a day is
/// the office-side dispatching workflow both roles live in, and neither operation is something a
/// technician's phone calls directly — it reaches the result through <c>GET /dispatch/board</c> and,
/// later, the sync endpoints (step 50).
/// </remarks>
public static class ScheduleEndpoints
{
    public static IEndpointRouteBuilder MapScheduleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var schedule = endpoints.MapGroup("/schedule")
            .RequireAuthorization(AuthPolicies.AdminOrDispatcher)
            .WithTags("Schedule");

        schedule.MapPost("/optimize", OptimizeAsync).WithName("OptimizeSchedule")
            .RequireRateLimiting(RateLimiting.OptimizePolicy)
            .Produces<OptimizeScheduleResponse>();
        schedule.MapPost("/insert", InsertAsync).WithName("InsertScheduleJob")
            .Produces<InsertJobResponse>();

        // The repair, and the way back from a day a dispatcher has dragged into overlapping itself
        // — which /insert refuses to work with. Not rate-limited beside /optimize: it re-times one
        // technician's run and does no search, so it is closer in cost to reading the board.
        schedule.MapPost("/normalize", NormalizeAsync).WithName("NormalizeSchedule")
            .Produces<NormalizeDayResponse>();

        return endpoints;
    }

    private static async Task<IResult> OptimizeAsync(
        OptimizeScheduleRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new OptimizeDayCommand(request.From, request.To, ToWeights(request.Weights));
        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(optimized => Results.Ok(ToResponse(optimized)));
    }

    private static async Task<IResult> InsertAsync(
        InsertJobRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new InsertJobCommand(JobId.From(request.JobId));
        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(inserted => Results.Ok(ToResponse(inserted)));
    }

    private static async Task<IResult> NormalizeAsync(
        NormalizeDayRequest request,
        ISender sender,
        CancellationToken cancellationToken)
    {
        var command = new NormalizeDayCommand(
            TechnicianId.From(request.TechnicianId), request.From, request.To);

        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(normalized => Results.Ok(ToResponse(normalized)));
    }

    private static ObjectiveWeights? ToWeights(ObjectiveWeightsRequest? weights) => weights is null
        ? null
        : new ObjectiveWeights(weights.Travel, weights.Lateness, weights.Overtime, weights.Unassigned);

    private static OptimizeScheduleResponse ToResponse(OptimizedDay optimized) => new(
        optimized.Planned,
        optimized.Unassigned.Select(jobId => jobId.Value).ToArray(),
        optimized.Cost);

    private static NormalizeDayResponse ToResponse(NormalizedDay normalized) => new(
        normalized.Stops,
        normalized.Moved,
        normalized.FirstStart,
        normalized.LastEnd);

    private static InsertJobResponse ToResponse(InsertedJob inserted) => new(
        inserted.AssignmentId.Value,
        inserted.TechnicianId.Value,
        inserted.ScheduledStart,
        inserted.Sequence,
        inserted.Displaced);
}
