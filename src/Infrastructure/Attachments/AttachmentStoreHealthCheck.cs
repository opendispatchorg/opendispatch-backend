using Microsoft.Extensions.Diagnostics.HealthChecks;
using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Infrastructure.Attachments;

/// <summary>
/// Whether this host can currently reach the place photographs and signatures live.
/// </summary>
/// <remarks>
/// <para>
/// The second readiness check, and the one whose absence this project has already been bitten by:
/// until it existed, <c>/health/ready</c> asked about the database and nothing else, so a host with
/// an unmounted volume or a mistyped bucket answered "healthy" and 500'd every upload a technician
/// made. Attachment content is the only data in this system that is not in Postgres, which is
/// exactly why a Postgres check cannot stand in for this one.
/// </para>
/// <para>
/// <strong>Degraded, not unhealthy.</strong> A shop whose object store is unreachable can still
/// dispatch, still take status changes, still invoice — everything except capture. Reporting
/// unhealthy would take the whole host out of rotation over one broken capability and stop the
/// dispatch board along with it. Readiness still answers 503 (the endpoint treats degraded as
/// not-ready, which is the correct yes/no for a load balancer), but the report names what is wrong
/// rather than implying the system is down.
/// </para>
/// </remarks>
public sealed class AttachmentStoreHealthCheck(IAttachmentStorage storage) : IHealthCheck
{
    /// <summary>The name this check is registered and reported under.</summary>
    public const string Name = "attachments";

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var reachable = await storage.IsReachableAsync(cancellationToken).ConfigureAwait(false);

        return reachable
            ? HealthCheckResult.Healthy("The attachment store is reachable.")
            : HealthCheckResult.Degraded(
                "The attachment store cannot be reached; uploads and downloads will fail.");
    }
}
