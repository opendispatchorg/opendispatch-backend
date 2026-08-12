using System.Security.Claims;
using System.Text.Json;
using MediatR;
using OpenDispatch.Api.Auth;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Sync;
using OpenDispatch.Application.Sync.PullChanges;
using OpenDispatch.Application.Sync.PushOps;
using OpenDispatch.Contracts.Sync;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Infrastructure.Auth;

namespace OpenDispatch.Api.Sync;

/// <summary>The offline sync protocol over HTTP: a device empties its queue, and asks what it missed
/// (Document 3, step 50).</summary>
/// <remarks>
/// <c>TechnicianOnly</c>, as the build text names — the one surface in this API a technician's
/// phone calls directly rather than an office worker's browser. Both routes read the calling
/// technician from the JWT (<see cref="AuthClaimTypes.Technician"/>) rather than the tenant's
/// pattern of an ambient <c>ITenantContext</c>: exactly two callers need it, both already take it
/// as an explicit parameter (<c>PushOpsCommand</c>, <c>PullChangesQuery</c>), and their own remarks
/// say a third caller is what would earn a port. This is that resolution, done at the edge.
/// </remarks>
public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sync = endpoints.MapGroup("/sync")
            .RequireAuthorization(AuthPolicies.TechnicianOnly)
            .WithTags("Sync");

        sync.MapPost("/push", PushAsync).WithName("PushSyncOps");
        sync.MapGet("/pull", PullAsync).WithName("PullSyncChanges");

        return endpoints;
    }

    private static async Task<IResult> PushAsync(
        SyncPushRequest request,
        ClaimsPrincipal caller,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (!TryGetTechnicianId(caller, out var technicianId))
        {
            return Results.Unauthorized();
        }

        var command = new PushOpsCommand(technicianId, [.. request.Ops.Select(ToPushedOp)]);
        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        return result.ToHttpResult(batch => Results.Ok(ToResponse(batch)));
    }

    private static async Task<IResult> PullAsync(
        SyncCursor since,
        ClaimsPrincipal caller,
        ISender sender,
        CancellationToken cancellationToken)
    {
        if (!TryGetTechnicianId(caller, out var technicianId))
        {
            return Results.Unauthorized();
        }

        var result = await sender
            .Send(new PullChangesQuery(technicianId, since), cancellationToken)
            .ConfigureAwait(false);

        return result.ToHttpResult(pulled => Results.Ok(ToResponse(pulled)));
    }

    /// <summary>
    /// Reads the calling technician off the validated principal.
    /// </summary>
    /// <remarks>
    /// Every token this system issues for a <c>Technician</c> role carries one — see
    /// <c>AuthUser.TechnicianId</c>'s remarks — so in ordinary operation this always succeeds. A
    /// missing or unparseable claim means a token this system did not issue in the usual way (a
    /// hand-crafted test token, or a technician seeded without a link), and is refused with a bare
    /// 401 ahead of the <c>Result</c>/<c>Error</c> machinery — the same register
    /// <c>TenantResolutionMiddleware</c> answers a missing org claim with at step 45.
    /// </remarks>
    private static bool TryGetTechnicianId(ClaimsPrincipal caller, out TechnicianId technicianId)
    {
        technicianId = default;
        var claim = caller.FindFirst(AuthClaimTypes.Technician)?.Value;

        if (!Guid.TryParse(claim, out var value))
        {
            return false;
        }

        technicianId = TechnicianId.From(value);

        return true;
    }

    private static PushedOp ToPushedOp(SyncOp op) => new(
        SyncOpId.From(op.Id), op.Entity, op.EntityId, op.Type, op.Payload, op.BaseVersion, op.ClientTs);

    private static SyncPushResponse ToResponse(PushedBatch batch) => new(
        [.. batch.Applied.Select(id => id.Value)],
        [.. batch.Conflicts.Select(ToConflict)],
        batch.Cursor.ToString());

    private static SyncConflict ToConflict(SyncOpConflict conflict) => new(
        conflict.OpId.Value, ToReason(conflict.Error.Code), conflict.Error.Message);

    /// <summary>
    /// The code → reason mapping step 42 deferred here. Two codes carry their own reason; every
    /// other conflict a push can produce — an operation this server does not know, a payload it
    /// cannot read, a job it does not have — is <see cref="SyncConflictReason.Unsupported"/>,
    /// exactly as step 42's own entry named it before this step existed to write it down.
    /// </summary>
    private static SyncConflictReason ToReason(string code) => code switch
    {
        JobErrors.IllegalTransitionCode => SyncConflictReason.IllegalTransition,
        SyncErrors.NotesSupersededCode => SyncConflictReason.VersionConflict,
        _ => SyncConflictReason.Unsupported,
    };

    private static SyncPullResponse ToResponse(PulledChanges pulled) => new(
        [
            .. pulled.Changes.Jobs.Select(ToChange),
            .. pulled.Changes.Stops.Select(ToChange),
            .. pulled.Changes.RemovedStops.Select(ToRemoval),
        ],
        pulled.Cursor.ToString());

    private static SyncChange ToChange(SyncJobState job) => new(
        FieldOps.JobEntity,
        job.JobId.Value,
        job.Version,
        JsonSerializer.SerializeToElement(ToPayload(job)),
        Deleted: false);

    private static SyncJobPayload ToPayload(SyncJobState job) => new(
        (Contracts.JobStatus)job.Status,
        (Contracts.JobPriority)job.Priority,
        job.RequiredSkill,
        job.Window.Start,
        job.Window.End,
        job.EstimatedDuration,
        job.Location.Lat,
        job.Location.Lng,
        job.CustomerName,
        job.Address,
        job.Notes,
        job.NotesRecordedAt,
        [.. job.Lines.Select(ToPayload)]);

    private static SyncJobLinePayload ToPayload(SyncJobLine line) => new(
        line.Id.Value, (Contracts.LineItemKind)line.Kind, line.Description, line.Quantity, line.UnitPrice.Cents / 100m);

    private static SyncChange ToChange(SyncStopState stop) => new(
        SyncRemoval.StopEntity,
        stop.AssignmentId.Value,
        stop.Version,
        JsonSerializer.SerializeToElement(ToPayload(stop)),
        Deleted: false);

    private static SyncStopPayload ToPayload(SyncStopState stop) => new(
        stop.JobId.Value, stop.Sequence, stop.ScheduledStart, stop.TravelMin);

    /// <summary>
    /// A stop that is gone, reported with nothing but its id — there is no state left to send, and
    /// no meaningful version for a row that no longer exists.
    /// </summary>
    private static SyncChange ToRemoval(AssignmentId id) => new(
        SyncRemoval.StopEntity, id.Value, Version: 0, State: null, Deleted: true);
}
