namespace OpenDispatch.Api.Health;

/// <summary>
/// The payload returned by <c>GET /health</c>.
/// </summary>
public sealed record HealthResponse(string Status, string Service);

/// <summary>
/// Liveness endpoint. Deliberately dependency-free: it reports that the host is up and
/// serving requests, not that downstream systems are reachable.
/// </summary>
public static class HealthEndpoints
{
    private static readonly HealthResponse Healthy = new("healthy", "opendispatch-api");

    public static IEndpointRouteBuilder MapHealthEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => TypedResults.Ok(Healthy))
            .WithName("Health");

        return endpoints;
    }
}
