using OpenDispatch.Api.Configuration;

namespace OpenDispatch.Api.ErrorHandling;

/// <summary>
/// A route with one job: throw, so <see cref="UnhandledExceptionHandler"/> has something to
/// answer for. Not temporary — unlike the auth/tenancy test endpoints steps 44/45 added and
/// step 47 removes now that real endpoints exist, nothing in ordinary business traffic
/// deliberately throws, so nothing supersedes this the way a real 404 superseded
/// <c>/_test/error-mapping/not-found</c>. It stays as a permanent, minimal regression fixture for
/// the exception handler, the same role <c>/health</c> plays for the host itself.
/// </summary>
/// <remarks>
/// <strong>It is not mapped outside the environments this repository controls.</strong> An
/// anonymous route whose whole purpose is to throw is, on a public host, a way to manufacture 500s
/// and error-log volume without credentials — and this one's exception message carries a
/// deliberately alarming fake connection string, which is fine in a test fixture and not fine in a
/// production log. The guard is the mapping, not a check inside the handler: where it is not
/// allowed, the route does not exist and answers 404 like any other unknown path.
/// </remarks>
public static class DiagnosticsEndpoints
{
    /// <summary>
    /// Maps the fixture where <see cref="DevelopmentDefaults.AreAllowedIn"/> allows it, and
    /// nowhere else.
    /// </summary>
    /// <param name="endpoints">The route builder.</param>
    /// <param name="environment">The host's environment.</param>
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(
        this IEndpointRouteBuilder endpoints,
        IHostEnvironment environment)
    {
        if (!DevelopmentDefaults.AreAllowedIn(environment))
        {
            return endpoints;
        }

        endpoints.MapGet("/_diagnostics/throws", Throws)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return endpoints;
    }

    private static IResult Throws() =>
        throw new InvalidOperationException("connection string: Host=prod-db;Password=hunter2");
}
