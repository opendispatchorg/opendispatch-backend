using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OpenDispatch.Api.Health;

/// <summary>
/// The payload returned by <c>GET /health</c> and <c>GET /health/live</c>.
/// </summary>
/// <param name="Status">Always <c>healthy</c>: a host that could not answer would not answer.</param>
/// <param name="Service">Which service replied, for a probe pointed at the wrong port.</param>
public sealed record HealthResponse(string Status, string Service);

/// <summary>
/// The payload returned by <c>GET /health/ready</c>.
/// </summary>
/// <param name="Status">
/// <c>healthy</c>, <c>degraded</c> or <c>unhealthy</c> — the worst status among the checks.
/// </param>
/// <param name="Service">Which service replied.</param>
/// <param name="Checks">
/// Each registered check by name, with its own status. An operator reading a 503 needs to know
/// which dependency is down; a single word makes them go and look.
/// </param>
public sealed record ReadinessResponse(
    string Status,
    string Service,
    IReadOnlyDictionary<string, string> Checks);

/// <summary>
/// Liveness and readiness, deliberately separate questions (Document 3, step 54).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Liveness asks whether this process is still working; readiness asks whether it can
/// currently do anything useful.</strong> They are separate because the reactions are opposite. A
/// host whose database has gone away should be taken out of rotation and left alone — it will
/// recover on its own, and restarting it fixes nothing while losing whatever it was doing. A host
/// that has stopped answering at all should be restarted. One endpoint answering both questions
/// makes an orchestrator restart every replica each time Postgres reboots, which is exactly the
/// outage-amplifying behaviour this split exists to prevent.
/// </para>
/// <para>
/// <strong>Liveness therefore checks nothing</strong>, on purpose: it is a constant, and its
/// answer means "the process is up, the pipeline is wired, and Kestrel is serving". Every health
/// check registered anywhere in this solution is a readiness check, so a check added later joins
/// readiness by existing and no tag vocabulary has to be kept in step. The day something genuinely
/// belongs to liveness, that is when tags earn their place.
/// </para>
/// <para>
/// <strong><c>GET /health</c> is kept, unchanged, as the liveness alias.</strong> It is what
/// step 3 shipped, what the README documents, and what anything already pointed at this service is
/// polling. Renaming it would be a breaking change to the one endpoint whose whole job is to be
/// reachable.
/// </para>
/// <para>
/// All three are anonymous. A probe runs before there is anybody to authenticate as, and neither
/// payload says anything a caller could not learn by sending one request.
/// </para>
/// </remarks>
public static class HealthEndpoints
{
    private const string ServiceName = "opendispatch-api";

    private static readonly HealthResponse Alive = new("healthy", ServiceName);

    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", Live)
            .WithName("Health")
            .WithTags("Health");

        endpoints.MapGet("/health/live", Live)
            .WithName("HealthLive")
            .WithTags("Health");

        endpoints.MapGet("/health/ready", ReadyAsync)
            .WithName("HealthReady")
            .WithTags("Health")
            .Produces<ReadinessResponse>()
            .Produces<ReadinessResponse>(StatusCodes.Status503ServiceUnavailable);

        return endpoints;
    }

    private static Ok<HealthResponse> Live() => TypedResults.Ok(Alive);

    /// <summary>
    /// Runs every registered health check and answers 200 or 503.
    /// </summary>
    /// <remarks>
    /// Written as a route handler over <see cref="HealthCheckService"/> rather than mapped through
    /// <c>MapHealthChecks</c>: the middleware's own response is a bare status word in
    /// <c>text/plain</c>, and this API answers JSON everywhere else, describes its responses in the
    /// OpenAPI document the clients are generated from, and already has a shape for "which service
    /// am I talking to". The service underneath is the framework's either way, so the checks, their
    /// timeouts and their aggregation are not reimplemented here — only the reply is.
    /// </remarks>
    private static async Task<IResult> ReadyAsync(HealthCheckService health, CancellationToken cancellationToken)
    {
        var report = await health.CheckHealthAsync(cancellationToken).ConfigureAwait(false);

        var body = new ReadinessResponse(
            Describe(report.Status),
            ServiceName,
            report.Entries.ToDictionary(entry => entry.Key, entry => Describe(entry.Value.Status)));

        // Degraded answers 503 alongside Unhealthy, because readiness is a yes/no question and a
        // host that is degraded is one an orchestrator should route around while it recovers. The
        // body still says which it was.
        return report.Status is HealthStatus.Healthy
            ? Results.Ok(body)
            : Results.Json(body, statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static string Describe(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "healthy",
        HealthStatus.Degraded => "degraded",
        _ => "unhealthy",
    };
}
