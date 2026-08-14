using System.Security.Claims;
using System.Text.Json;
using MediatR;
using Microsoft.Extensions.Options;
using OpenDispatch.Api.Auth;
using OpenDispatch.Api.Configuration;
using OpenDispatch.Api.ErrorHandling;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Observability;
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
/// <para>
/// <strong>A technician may act on any job in their organization, not only the ones planned for
/// them.</strong> A push names the acting technician from the token and the job from the operation,
/// and the two are not required to match. That is deliberate rather than an oversight of scoping:
/// field workers cover for each other, a dispatcher reassigns work mid-morning, and a phone holding
/// a stop that moved ten minutes ago would otherwise have its perfectly good "I am on site"
/// rejected. The tenant boundary is the one that matters and it is enforced by the query filters;
/// within a shop, a shop's own technicians are trusted with a shop's own jobs.
/// </para>
/// <para>
/// The same reasoning covers the attachment upload beside it. Both are recorded against the
/// technician who acted (<c>SyncOpRecord.TechnicianId</c>), so who did what is answerable after the
/// fact, which is the property that makes the trust affordable.
/// </para>
/// <para>
/// <c>TechnicianOnly</c>, as the build text names — the one surface in this API a technician's
/// phone calls directly rather than an office worker's browser. Both routes read the calling
/// technician from the JWT (<see cref="AuthClaimTypes.Technician"/>) rather than the tenant's
/// pattern of an ambient <c>ITenantContext</c>: exactly two callers need it, both already take it
/// as an explicit parameter (<c>PushOpsCommand</c>, <c>PullChangesQuery</c>), and their own remarks
/// say a third caller is what would earn a port. This is that resolution, done at the edge.
/// </para>
/// </remarks>
public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var sync = endpoints.MapGroup("/sync")
            .RequireAuthorization(AuthPolicies.TechnicianOnly)
            .WithTags("Sync");

        // Both routes also answer a bare, bodyless 401 when the token's own tech claim is
        // missing or unparseable (TryGetTechnicianId) — ahead of the Result/Error machinery
        // the shared "default" ProblemDetails response otherwise describes, so it is named
        // explicitly here rather than left for that generic entry to (incorrectly) cover.
        sync.MapPost("/push", PushAsync).WithName("PushSyncOps")
            .Produces<SyncPushResponse>()
            .Produces(StatusCodes.Status401Unauthorized);
        sync.MapGet("/pull", PullAsync).WithName("PullSyncChanges")
            .Produces<SyncPullResponse>()
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    /// <remarks>
    /// <para>
    /// <strong>The step-54 counters are recorded here rather than in the handler</strong>, and the
    /// difference is when: the handler runs inside the transaction, so counting there counted work
    /// that a failed save could still take back — a batch applied and then lost was reported as
    /// field work, over-reporting precisely when something had gone wrong. A successful
    /// <c>Result</c> at this point is a committed one (<c>TransactionBehavior</c> rolls back
    /// anything else), so this is the first place the numbers are true.
    /// </para>
    /// <para>
    /// Applied is counted once for the batch rather than per operation — <c>Add(n)</c> is one
    /// measurement where n increments are n — and includes ops an earlier push had already applied,
    /// because they are what this device believes it did; undercounting them would make a phone
    /// stuck in a retry loop look idle. Conflicts carry their reason as a tag, which is the only
    /// place a client shipping operations this server cannot apply becomes visible at all: a
    /// refusal rides inside a 200.
    /// </para>
    /// </remarks>
    private static async Task<IResult> PushAsync(
        SyncPushRequest request,
        ClaimsPrincipal caller,
        ISender sender,
        SyncMetrics metrics,
        CancellationToken cancellationToken)
    {
        if (!TryGetTechnicianId(caller, out var technicianId))
        {
            return Results.Unauthorized();
        }

        var command = new PushOpsCommand(technicianId, [.. request.Ops.Select(ToPushedOp)]);
        var result = await sender.Send(command, cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            metrics.Applied(result.Value.Applied.Count);

            foreach (var conflict in result.Value.Conflicts)
            {
                metrics.Conflicted(conflict.Error.Code);
            }
        }

        return result.ToHttpResult(batch => Results.Ok(ToResponse(batch)));
    }

    /// <remarks>
    /// The page size is read here, from options resolved per request, rather than being a constant
    /// in the handler or something the caller may ask for: a device does not get to request the
    /// whole database, and an operator with a slow fleet gets a lever. Per request, because
    /// configuration read while the container is being built is read before a host's own sources
    /// are applied — the lesson the edge-hardening pass wrote down.
    /// </remarks>
    private static async Task<IResult> PullAsync(
        SyncCursor since,
        ClaimsPrincipal caller,
        ISender sender,
        IOptions<SyncOptions> sync,
        CancellationToken cancellationToken)
    {
        if (!TryGetTechnicianId(caller, out var technicianId))
        {
            return Results.Unauthorized();
        }

        var result = await sender
            .Send(
                new PullChangesQuery(technicianId, since, sync.Value.PullPageTransactions),
                cancellationToken)
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
        pulled.Cursor.ToString(),
        pulled.HasMore);

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
