using OpenDispatch.Application.Results;

namespace OpenDispatch.Api.ErrorHandling;

/// <summary>
/// Endpoints that exist only to prove <see cref="ResultHttpMapping"/> and
/// <see cref="UnhandledExceptionHandler"/> end to end (Document 3, step 46) — the categories
/// <c>/auth/login</c> does not already exercise for real (<c>Validation</c> and
/// <c>Unauthorized</c> ride along with the real login flow; nothing yet returns
/// <c>NotFound</c>/<c>Conflict</c> or throws).
/// </summary>
public static class ErrorMappingTestEndpoints
{
    public static IEndpointRouteBuilder MapErrorMappingTestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // TEMPORARY: removed in step 47, once a real endpoint (GET /customers/{id}, most likely)
        // returns a NotFound Result for real and its own tests cover this ground.
        endpoints.MapGet("/_test/error-mapping/not-found", NotFound)
            .AllowAnonymous()
            .ExcludeFromDescription();

        // TEMPORARY: removed in step 47. Proves the exception handler answers a bug with a
        // generic 500 rather than whatever the bug happened to be holding.
        endpoints.MapGet("/_test/error-mapping/throws", Throws)
            .AllowAnonymous()
            .ExcludeFromDescription();

        return endpoints;
    }

    private static IResult NotFound() =>
        Result.Failure(Error.NotFound("test.notFound", "Nothing here, on purpose.")).ToHttpResult();

    private static IResult Throws() =>
        throw new InvalidOperationException("connection string: Host=prod-db;Password=hunter2");
}
