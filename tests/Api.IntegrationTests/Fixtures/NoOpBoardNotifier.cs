using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Api.IntegrationTests.Fixtures;

/// <summary>
/// <see cref="IBoardNotifier"/> over nothing — <c>TestHost</c>'s stand-in for the real SignalR
/// adapter, which lives in <c>Api</c> and needs a running host to construct
/// (<c>IHubContext&lt;DispatchHub&gt;</c>).
/// </summary>
/// <remarks>
/// Every test that boots the application pipeline through <c>TestHost</c> raises domain events
/// step 51's <c>BoardNotifications</c> subscribes to — that subscription is unconditional, the
/// whole point of the scan-and-subscribe extension seam (Document 2 §12) — so something has to
/// satisfy the port even in a host with nothing listening on the other end. Tests that actually
/// care whether a board event fired go through <see cref="ApiFactory"/> instead, where the real
/// registration in <c>Program.cs</c> applies.
/// </remarks>
internal sealed class NoOpBoardNotifier : IBoardNotifier
{
    public Task JobUpdatedAsync(JobId jobId, JobStatus status, long version, CancellationToken ct) =>
        Task.CompletedTask;

    public Task AssignmentUpdatedAsync(
        AssignmentId assignmentId,
        JobId jobId,
        TechnicianId technicianId,
        int sequence,
        DateTimeOffset scheduledStart,
        double travelMin,
        long version,
        CancellationToken ct) =>
        Task.CompletedTask;
}
