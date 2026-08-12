using MediatR;
using OpenDispatch.Api.Auth;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Dispatch.GetBoard;
using OpenDispatch.Contracts.Board;

namespace OpenDispatch.Api.Dispatch;

/// <summary>The dispatch board, read over REST (Document 3, step 48).</summary>
/// <remarks>
/// <c>AdminOrDispatcher</c>, matching Schedule beside it: the board is where a dispatcher watches
/// the plan Schedule's two endpoints produce.
/// </remarks>
public static class DispatchEndpoints
{
    public static IEndpointRouteBuilder MapDispatchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var dispatch = endpoints.MapGroup("/dispatch")
            .RequireAuthorization(AuthPolicies.AdminOrDispatcher)
            .WithTags("Dispatch");

        dispatch.MapGet("/board", GetBoardAsync).WithName("GetDispatchBoard");

        return endpoints;
    }

    /// <summary>
    /// Turns the <c>day</c> the build text names into the horizon <see cref="GetBoardQuery"/> wants.
    /// </summary>
    /// <remarks>
    /// A date is not an instant until somebody names a timezone, and nothing in this system models
    /// one (Document 2 §2 gives <c>Organization</c> none). Rather than invent one at the edge, a day
    /// is read as UTC: <c>day</c> means <c>[day 00:00 UTC, day+1 00:00 UTC)</c>. A caller in another
    /// timezone converts before asking, the same way it would if the board were a real person's
    /// working day rather than a calendar date — which step 20's own entry already names as the
    /// harder version of this same question.
    /// </remarks>
    private static async Task<IResult> GetBoardAsync(DateOnly day, ISender sender, CancellationToken cancellationToken)
    {
        var start = new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var query = new GetBoardQuery(start, start.AddDays(1));
        var result = await sender.Send(query, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(board => Results.Ok(ToResponse(board)));
    }

    private static DispatchBoardResponse ToResponse(DispatchBoard board) => new(
        board.Day.Start,
        board.Day.End,
        board.Routes.Select(ToResponse).ToArray(),
        board.Unassigned.Select(ToResponse).ToArray());

    private static BoardRouteResponse ToResponse(BoardRoute route) => new(
        route.TechnicianId.Value,
        route.Name,
        route.Skills,
        route.Shift.Start,
        route.Shift.End,
        route.HomeBase.Lat,
        route.HomeBase.Lng,
        route.Stops.Select(ToResponse).ToArray());

    private static BoardStopResponse ToResponse(BoardStop stop) => new(
        stop.AssignmentId.Value,
        stop.Sequence,
        stop.ScheduledStart,
        stop.TravelMin,
        stop.LateBy,
        ToResponse(stop.Job));

    private static BoardJobResponse ToResponse(BoardJob job) => new(
        job.JobId.Value,
        (Contracts.JobStatus)job.Status,
        (Contracts.JobPriority)job.Priority,
        job.RequiredSkill,
        job.Window.Start,
        job.Window.End,
        job.EstimatedDuration,
        job.Location.Lat,
        job.Location.Lng,
        job.CustomerName,
        job.Address);
}
