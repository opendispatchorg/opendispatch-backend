namespace OpenDispatch.Api.ErrorHandling;

/// <summary>
/// A route with one job: throw, so <see cref="UnhandledExceptionHandler"/> has something to
/// answer for. Not temporary — unlike the auth/tenancy test endpoints steps 44/45 added and
/// step 47 removes now that real endpoints exist, nothing in ordinary business traffic
/// deliberately throws, so nothing supersedes this the way a real 404 superseded
/// <c>/_test/error-mapping/not-found</c>. It stays as a permanent, minimal regression fixture for
/// the exception handler, the same role <c>/health</c> plays for the host itself.
/// </summary>
public static class DiagnosticsEndpoints
{
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/_diagnostics/throws", Throws)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return endpoints;
    }

    private static IResult Throws() =>
        throw new InvalidOperationException("connection string: Host=prod-db;Password=hunter2");
}
