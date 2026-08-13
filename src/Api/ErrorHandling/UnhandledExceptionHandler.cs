using Microsoft.AspNetCore.Diagnostics;

namespace OpenDispatch.Api.ErrorHandling;

/// <summary>
/// The last resort: an exception nothing turned into a <c>Result</c> on the way up (Document 3,
/// step 46). Logs the real thing — message, stack trace, everything — server-side, and answers
/// with a ProblemDetails body that says none of it.
/// </summary>
/// <remarks>
/// <para>
/// Registered in <c>Program.cs</c> behind <c>UseExceptionHandler()</c>, which is placed after
/// <c>UseSerilogRequestLogging</c> and before everything else: this handler catches an exception
/// and writes a response rather than rethrowing, so the request-logging middleware — which wraps
/// it — sees an ordinary completed 500 response and logs a summary line, not a second dump of
/// the same exception. This is the one place the exception itself is logged.
/// </para>
/// <para>
/// The 500 is exactly as generic for a bug in this handler's own code as for one anywhere else:
/// there is no second try/catch here, and there is not meant to be. If writing the ProblemDetails
/// response itself throws, that is a bug worth a crash, not a bug worth hiding better.
/// </para>
/// </remarks>
internal sealed class UnhandledExceptionHandler(
    IProblemDetailsService problemDetails,
    ILogger<UnhandledExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        UnhandledExceptionHandlerLog.Unhandled(
            logger, exception, httpContext.Request.Method, httpContext.Request.Path, httpContext.TraceIdentifier);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",

                // Never the exception's own message: it can hold a connection string, a SQL
                // fragment, a file path — whatever the thing that broke happened to be holding.
                Detail = "Something went wrong while handling this request.",
            },
        }).ConfigureAwait(false);
    }
}
