using Microsoft.AspNetCore.Http.Timeouts;
using OpenDispatch.Api.Attachments;

namespace OpenDispatch.Api.Security;

/// <summary>
/// How long a request may take and how large it may be.
/// </summary>
/// <remarks>
/// <para>
/// Neither had a bound before. A request could run until the client gave up, holding a database
/// connection the whole time, and Kestrel's default 30 MB body limit sat <em>above</em> the 25 MB
/// attachment cap — so the cap was enforced by <c>AttachmentEndpoints</c> after the bytes had
/// already been read off the socket and buffered. A caller could make this host accept thirty
/// megabytes and then refuse them, which is the wrong order to do those two things in.
/// </para>
/// <para>
/// <strong>These are limits, not tuning.</strong> Every number here is far above every measured
/// path — the load pass's worst deliberate abuse was a 1.4-second optimise and a 5-second export —
/// and exists to stop a request running or growing without end, not to shape ordinary traffic.
/// </para>
/// </remarks>
public static class RequestLimits
{
    /// <summary>
    /// The largest request body this host will read.
    /// </summary>
    /// <remarks>
    /// The attachment cap plus a megabyte, because a multipart upload carries boundaries, part
    /// headers and a form field around the file — a limit set to exactly the cap would refuse a
    /// 25 MB photograph that is entirely within it. The cap itself still lives on the endpoint,
    /// which is where a caller gets told the real number in words; this is the outer wall that
    /// stops the bytes arriving at all.
    /// </remarks>
    public const long MaxRequestBodyBytes = AttachmentEndpoints.MaxContentLength + (1024 * 1024);

    /// <summary>The policy name for the one request that is legitimately long.</summary>
    public const string LongRunningPolicy = "long-running";

    /// <summary>
    /// The default ceiling on a request.
    /// </summary>
    /// <remarks>
    /// Thirty seconds: an order of magnitude above the slowest budget in the README (five seconds
    /// for an export of a year's history) and below any client's own patience. A request still
    /// running at thirty seconds is wedged, and cancelling it returns a database connection that a
    /// caller who has already closed their laptop was still holding.
    /// </remarks>
    private static readonly TimeSpan Default = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The ceiling on <c>GET /export</c>.
    /// </summary>
    /// <remarks>
    /// The export streams a whole tenant's history and is the one request in this API whose honest
    /// duration grows with the shop. Five minutes is generous for the largest dataset the load pass
    /// measured and still finite, which is the point — an export that has been running for five
    /// minutes has hit something other than size.
    /// </remarks>
    private static readonly TimeSpan LongRunning = TimeSpan.FromMinutes(5);

    /// <summary>Registers the request-timeout policies and the body-size limit.</summary>
    public static IServiceCollection AddRequestLimits(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddRequestTimeouts(options =>
        {
            // Applied to every endpoint that does not say otherwise, so a route added later is
            // bounded by existing rather than by somebody remembering.
            options.DefaultPolicy = new RequestTimeoutPolicy
            {
                Timeout = Default,

                // 503 rather than the framework's default 504: nothing gatewayed here, and this
                // host is telling a caller it could not finish in time, which is the same shape as
                // a readiness refusal.
                TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable,
            };

            options.AddPolicy(
                LongRunningPolicy,
                new RequestTimeoutPolicy
                {
                    Timeout = LongRunning,
                    TimeoutStatusCode = StatusCodes.Status503ServiceUnavailable,
                });
        });

        // Kestrel's own limit, so oversized bodies are refused by the server before any handler
        // sees them. IIS and HTTP.sys have their own; a deployment behind either sets those too.
        services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(
            options => options.Limits.MaxRequestBodySize = MaxRequestBodyBytes);

        return services;
    }
}
