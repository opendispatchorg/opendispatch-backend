using Serilog.Context;

namespace OpenDispatch.Api.Observability;

/// <summary>
/// Gives every request one id that the caller, the response body, the response headers and every
/// log line the request produces all agree on (Document 3, step 54).
/// </summary>
/// <remarks>
/// <para>
/// <strong>It sets <see cref="HttpContext.TraceIdentifier"/> rather than inventing a parallel
/// id.</strong> That one assignment is what makes the id turn up everywhere without further
/// wiring: <c>AddProblemDetails</c>'s <c>CustomizeProblemDetails</c> hook already puts
/// <c>TraceIdentifier</c> on every failure body as <c>traceId</c> (step 46), the request-summary
/// line already prints it, and <c>UnhandledExceptionHandler</c> already logs it. A second id
/// beside it would mean four surfaces carrying two values, which is worse than carrying none.
/// </para>
/// <para>
/// <strong>An inbound <c>X-Correlation-ID</c> wins, when it is one this server is willing to
/// repeat.</strong> A support question starts on a phone in a basement or in a dispatcher's
/// browser, and the id that answers it has to be one the client already knows — otherwise the
/// client's own log and the server's cannot be joined at all. Kestrel's per-connection
/// <c>TraceIdentifier</c> is the fallback for callers that send nothing, which is every caller
/// today; both clients are free to start sending one without a change here.
/// </para>
/// <para>
/// <strong>What arrives is not trusted verbatim.</strong> It is echoed into a response header and
/// written into log lines, so a caller that could put a newline or a hundred kilobytes in it could
/// forge log entries or split a response header. Anything that is not a short run of printable
/// ASCII is discarded in favour of the server's own id rather than sanitised into something the
/// caller did not send — a half-kept id correlates nothing and hides that it was rejected.
/// </para>
/// <para>
/// <strong>The id is pushed onto Serilog's <see cref="LogContext"/> for the life of the
/// request</strong>, so a handler's own log lines — <c>PipelineLog</c>'s, in particular, which
/// deliberately log a request's type and never its contents — carry it as a structured property
/// and can be joined to the request-summary line. Whether a given sink prints that property is the
/// sink's business: the two lines that have to be greppable in the default console configuration
/// (the request summary and the unhandled-exception dump) name it in their own message templates,
/// because Serilog's console renderer prints a template's tokens and not the event's whole
/// property set.
/// </para>
/// </remarks>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>The header a client sends its own id in, and the one every response carries back.</summary>
    public const string HeaderName = "X-Correlation-ID";

    /// <summary>The property name every log event in the request carries the id under.</summary>
    public const string LogPropertyName = "CorrelationId";

    /// <summary>
    /// Longest inbound id this server will repeat. Comfortably above a UUID or a W3C trace id and
    /// well below anything that could be used to pad a log file.
    /// </summary>
    private const int MaxLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = FromCaller(context.Request) ?? context.TraceIdentifier;

        context.TraceIdentifier = correlationId;

        // Written as the response starts rather than here, and that is not caution — it is the
        // only thing that works. UseExceptionHandler clears the response, headers included, before
        // it writes its 500, so a header set on the way in is gone from exactly the responses whose
        // id somebody is going to quote. Verified against a running host: set inline, the 500 from
        // /_diagnostics/throws carried the id in its body and not in its headers.
        context.Response.OnStarting(
            static state =>
            {
                var starting = (HttpContext)state;
                starting.Response.Headers[HeaderName] = starting.TraceIdentifier;

                return Task.CompletedTask;
            },
            context);

        using (LogContext.PushProperty(LogPropertyName, correlationId))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The caller's own id, if it sent exactly one and it is safe to repeat.
    /// </summary>
    /// <remarks>
    /// Several values means an intermediary appended rather than replaced; there is no way to
    /// choose between them, so the server's own id is used instead of guessing.
    /// </remarks>
    private static string? FromCaller(HttpRequest request)
    {
        var header = request.Headers[HeaderName];

        if (header.Count != 1)
        {
            return null;
        }

        var candidate = header[0]?.Trim();

        return IsSafe(candidate) ? candidate : null;
    }

    private static bool IsSafe(string? candidate)
    {
        if (string.IsNullOrEmpty(candidate) || candidate.Length > MaxLength)
        {
            return false;
        }

        foreach (var character in candidate)
        {
            // Printable ASCII only: no control characters (a newline forges a log line, a carriage
            // return splits a header), and nothing non-ASCII, which has no business in an
            // identifier a human is expected to copy out of one system and paste into another.
            if (character is < ' ' or > '~')
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Registration for <see cref="CorrelationIdMiddleware"/>.</summary>
public static class CorrelationIdMiddlewareExtensions
{
    /// <summary>
    /// Adds the correlation id to the pipeline. Belongs first, ahead of request logging and the
    /// exception handler: everything below it is what the id exists to tie together.
    /// </summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();
}
