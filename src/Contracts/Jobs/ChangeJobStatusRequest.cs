namespace OpenDispatch.Contracts.Jobs;

/// <summary>The body of <c>POST /jobs/{id}/status</c>.</summary>
/// <param name="Status">Where the job should be.</param>
/// <param name="CompletedAt">
/// When the work actually finished. Only read when <paramref name="Status"/> is
/// <see cref="JobStatus.Completed"/>; when it is omitted the server asks its own clock.
/// </param>
public sealed record ChangeJobStatusRequest(JobStatus Status, DateTimeOffset? CompletedAt = null);
