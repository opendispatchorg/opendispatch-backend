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
/// <strong>One exception is not a bug and is not answered as one:
/// <see cref="BadHttpRequestException"/>.</strong> It is what the framework throws when it cannot
/// read the request at all — a required query parameter that was not sent, a body that is not the
/// JSON the endpoint declared, a body larger than the server accepts — and it carries the status
/// code it wants (400, 413) rather than meaning "this server broke". Answering those with 500
/// blamed the server for the caller's request and, worse, told a client to retry something that
/// will fail identically forever. It is checked first, logged at warning rather than error, and
/// its own message is safe to return: the framework wrote it, from the endpoint's signature, and
/// it names the parameter rather than anything the caller sent.
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
        if (exception is BadHttpRequestException malformed)
        {
            UnhandledExceptionHandlerLog.Malformed(
                logger,
                httpContext.Request.Method,
                httpContext.Request.Path,
                malformed.StatusCode,
                malformed.Message,
                httpContext.TraceIdentifier);

            return await WriteAsync(
                httpContext,
                malformed.StatusCode,
                "The request could not be read.",
                malformed.Message).ConfigureAwait(false);
        }

        UnhandledExceptionHandlerLog.Unhandled(
            logger, exception, httpContext.Request.Method, httpContext.Request.Path, httpContext.TraceIdentifier);

        return await WriteAsync(
            httpContext,
            StatusCodes.Status500InternalServerError,
            "An unexpected error occurred.",

            // Never the exception's own message: it can hold a connection string, a SQL
            // fragment, a file path — whatever the thing that broke happened to be holding.
            "Something went wrong while handling this request.").ConfigureAwait(false);
    }

    private async ValueTask<bool> WriteAsync(HttpContext httpContext, int status, string title, string detail)
    {
        httpContext.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                Detail = detail,
            },
        }).ConfigureAwait(false);
    }
}
