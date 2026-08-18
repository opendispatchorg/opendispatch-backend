using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OpenDispatch.Infrastructure.Persistence;

/// <summary>
/// Whether this host can currently reach its database (Document 3, step 54).
/// </summary>
/// <remarks>
/// <para>
/// The readiness half of the split health check. Every endpoint in this system but two reads or
/// writes Postgres, so a host that cannot reach it is a host that should be taken out of a load
/// balancer's rotation — while still being left alone rather than restarted, which is the whole
/// reason liveness and readiness are separate questions. A database that comes back needs no new
/// process.
/// </para>
/// <para>
/// <strong>Connectivity, not correctness.</strong> <c>CanConnectAsync</c> opens a connection and
/// nothing more: it does not query an entity (so it needs no tenant — see
/// <c>AppDbContext.CurrentOrgId</c>), and it does not check that the schema is migrated. A
/// readiness probe that ran a real query would be a probe that can fail for a reason the
/// deployment cannot fix by waiting, and one that ran per-second against every replica for the
/// life of the deployment.
/// </para>
/// <para>
/// It reports rather than throws. <c>CanConnectAsync</c> swallows the provider's exception and
/// answers false, which is the right shape here — an unreachable database is an expected state of
/// the world for a probe, not an error — but it also means the reason is not in the result. The
/// details live in the connection failure this host's own logs record when a request actually
/// tries to use the database.
/// </para>
/// </remarks>
public sealed class DatabaseHealthCheck(AppDbContext database) : IHealthCheck
{
    /// <summary>The name this check is registered and reported under.</summary>
    public const string Name = "database";

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var reachable = await database.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false);

        return reachable
            ? HealthCheckResult.Healthy("The database is reachable.")
            : HealthCheckResult.Unhealthy("The database cannot be reached.");
    }
}
