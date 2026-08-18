namespace OpenDispatch.Api.Security;

/// <summary>
/// The handful of response headers a browser reads before it decides what it is allowed to do with
/// what this API sent it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An API that serves no HTML has no reason not to set these</strong>, and one reason to:
/// it does serve bytes somebody uploaded (<c>GET /attachments/{id}/content</c>), and it is reached
/// from a browser by two client applications. The download path already sets <c>nosniff</c> for
/// itself — that was the only security header anywhere in this host. Everything else was bare.
/// </para>
/// <para>
/// What each one is actually for, since a list of headers copied from a blog post is how a
/// deployment ends up with a policy nobody can explain:
/// </para>
/// <list type="bullet">
///   <item><description>
///     <c>X-Content-Type-Options: nosniff</c> — a browser must not second-guess a declared content
///     type. On the attachment path that is the difference between a photograph and whatever a
///     sniffer decides a photograph's bytes resemble.
///   </description></item>
///   <item><description>
///     <c>X-Frame-Options: DENY</c> and <c>Content-Security-Policy: frame-ancestors 'none'</c> —
///     the same instruction twice, for old browsers and current ones. Nothing here should ever be
///     in a frame; a JSON API in a frame is somebody's clickjacking setup or somebody's mistake.
///   </description></item>
///   <item><description>
///     <c>Referrer-Policy: no-referrer</c> — this API's URLs carry ids (a job, an invoice, an
///     attachment) and there is no third party they should travel to. Nothing here is linked from,
///     so nothing is lost by sending none.
///   </description></item>
///   <item><description>
///     <c>Permissions-Policy</c> — a short denial of the device capabilities a JSON response has no
///     business asking for. Minimal on purpose: naming every feature in the specification would be
///     a list that goes stale, and the three here are the ones that matter for a document served
///     from this origin.
///   </description></item>
/// </list>
/// <para>
/// <strong>HSTS is conditional, and that is the one that could break a deployment.</strong>
/// <c>Strict-Transport-Security</c> tells a browser never to speak to this origin over plain HTTP
/// again, for a year — so sending it from a host reached over HTTP would lock a developer, or a
/// shop on an internal network, out of their own system in a way no server-side change can undo.
/// It is sent only when the request actually arrived over HTTPS, which behind a proxy means
/// <c>ReverseProxy:Enabled</c> is on and the forwarded scheme said so. On Render that is always
/// true; on a laptop it never is.
/// </para>
/// <para>
/// Written on <c>OnStarting</c> rather than inline, for the reason <c>CorrelationIdMiddleware</c>
/// gives: <c>UseExceptionHandler</c> clears the response and its headers before writing a 500, so
/// headers set on the way in are missing from exactly the responses somebody is looking at.
/// </para>
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>One year, the value HSTS preload lists require and browsers expect.</summary>
    private const string StrictTransportSecurity = "max-age=31536000; includeSubDomains";

    /// <summary>
    /// The capabilities denied outright. Three rather than thirty: a JSON API asking for a camera
    /// is a compromise, and an exhaustive list is one that rots.
    /// </summary>
    private const string PermissionsPolicy = "camera=(), microphone=(), geolocation=()";

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Response.OnStarting(
            static state =>
            {
                var starting = (HttpContext)state;
                var headers = starting.Response.Headers;

                headers.XContentTypeOptions = "nosniff";
                headers.XFrameOptions = "DENY";
                headers.ContentSecurityPolicy = "frame-ancestors 'none'";
                headers["Referrer-Policy"] = "no-referrer";
                headers["Permissions-Policy"] = PermissionsPolicy;

                // Only over TLS. See the remarks: sent over HTTP it is unrecoverable from the
                // server side, and IsHttps is what UseForwardedHeaders has already corrected for a
                // host that says it is behind a proxy.
                if (starting.Request.IsHttps)
                {
                    headers.StrictTransportSecurity = StrictTransportSecurity;
                }

                return Task.CompletedTask;
            },
            context);

        await next(context).ConfigureAwait(false);
    }
}

/// <summary>Pipeline registration for <see cref="SecurityHeadersMiddleware"/>.</summary>
public static class SecurityHeadersMiddlewareExtensions
{
    /// <summary>
    /// Adds the security response headers to every response this host sends.
    /// </summary>
    /// <remarks>
    /// Placed beside the correlation id and for the same reason: both want to be above everything
    /// that can write a response, including the exception handler, so that a 500 and a 404 carry
    /// them as surely as a 200 does.
    /// </remarks>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        return app.UseMiddleware<SecurityHeadersMiddleware>();
    }
}
