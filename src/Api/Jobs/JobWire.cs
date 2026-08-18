using OpenDispatch.Application.Jobs;
using OpenDispatch.Contracts.Jobs;
using OpenDispatch.Contracts.Sync;

namespace OpenDispatch.Api.Jobs;

/// <summary>
/// A job on the wire, for the two endpoints that publish one.
/// </summary>
/// <remarks>
/// <para>
/// <c>GET /jobs/{id}</c>, <c>GET /jobs</c> and <c>GET /export</c> all answer with
/// <see cref="JobResponse"/>, and until now the mapping was written twice — once in
/// <c>JobEndpoints</c> and once in <c>ExportEndpoints</c>, identical field for field. Two copies
/// were tolerable while the record was flat; adding the recorded lines makes it a nested projection
/// with a money conversion in it, and the copy that gets a field late is the one a client discovers
/// by finding it missing from the export.
/// </para>
/// <para>
/// It maps a job's lines to <see cref="SyncJobLinePayload"/>, the shape <c>/sync/pull</c> already
/// publishes them in — see <see cref="JobResponse"/> for why that is reuse rather than coupling.
/// </para>
/// </remarks>
internal static class JobWire
{
    /// <summary>Cents to whole currency units, the conversion every price on the wire goes through.</summary>
    private const decimal CentsPerUnit = 100m;

    public static JobResponse ToResponse(JobSummary job) => new(
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
        job.Notes,
        job.ErasedAt,
        [.. job.Lines.Select(ToPayload)]);

    private static SyncJobLinePayload ToPayload(JobLineSummary line) => new(
        line.Id.Value,
        (Contracts.LineItemKind)line.Kind,
        line.Description,
        line.Quantity,
        line.UnitPrice.Cents / CentsPerUnit);
}
