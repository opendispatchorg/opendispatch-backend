using Microsoft.AspNetCore.SignalR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Contracts.Board;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Api.Board;

/// <summary>
/// <see cref="IBoardNotifier"/> over a real <see cref="DispatchHub"/> connection (Document 2 §9,
/// step 51).
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>Api</c> rather than <c>Infrastructure</c> beside every other adapter this codebase
/// has built — the one deliberate exception, forced by the hub itself. <c>IHubContext&lt;DispatchHub&gt;</c>
/// can only be resolved where <see cref="DispatchHub"/> is visible, and the hub cannot live in
/// <c>Infrastructure</c> without reaching upward into <c>Api</c>'s <c>AuthPolicies</c> for its own
/// authorization — the one dependency direction this codebase does not allow. Document 2 §2 already
/// lists SignalR hubs under <c>Api</c>'s own inventory, not <c>Infrastructure</c>'s, so this is the
/// hub's adapter following the hub rather than the general rule.
/// </para>
/// <para>
/// Reads <see cref="ITenantContext"/> normally, unlike the hub's own connection handling: this
/// class is resolved and called from inside the request that raised the domain event
/// (<c>IDomainEventHandler</c>'s own guarantee — "runs inside the same request"), so the ambient
/// tenant middleware already populated for that request is exactly right here.
/// </para>
/// </remarks>
internal sealed class SignalRBoardNotifier(IHubContext<DispatchHub> hub, ITenantContext tenant) : IBoardNotifier
{
    public Task JobUpdatedAsync(JobId jobId, JobStatus status, long version, CancellationToken ct) =>
        Group().SendAsync(
            BoardEvents.JobUpdated,
            new JobUpdated(jobId.Value, (Contracts.JobStatus)status, version),
            ct);

    public Task AssignmentUpdatedAsync(
        AssignmentId assignmentId,
        JobId jobId,
        TechnicianId technicianId,
        int sequence,
        DateTimeOffset scheduledStart,
        double travelMin,
        long version,
        CancellationToken ct) =>
        Group().SendAsync(
            BoardEvents.AssignmentUpdated,
            new AssignmentUpdated(assignmentId.Value, jobId.Value, technicianId.Value, sequence, scheduledStart, travelMin, version),
            ct);

    private IClientProxy Group() => hub.Clients.Group(BoardGroups.NameFor(tenant.OrgId));
}
