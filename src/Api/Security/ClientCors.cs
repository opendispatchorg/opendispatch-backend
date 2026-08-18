using Microsoft.AspNetCore.Cors.Infrastructure;

namespace OpenDispatch.Api.Security;

/// <summary>
/// Which browser origins may call this API.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Without this, the two clients this backend exists to serve cannot call it at all.</strong>
/// The dispatch board and the technician app are separate deployments on separate origins
/// (Documents 4–7), and a browser refuses a cross-origin call the server has not allowed. It went
/// unnoticed through the whole build because every test here speaks HTTP directly, and no browser
/// has ever pointed at this host.
/// </para>
/// <para>
/// <strong>Configured origins only — never <c>AllowAnyOrigin</c>.</strong> A wildcard would let any
/// page on the internet call this API from a victim's browser, and it buys nothing: a deployment
/// knows its own client origins, and a self-hosting shop sets one line of configuration.
/// </para>
/// <para>
/// <strong>No credentials.</strong> This API authenticates with a bearer token the client holds and
/// sends deliberately, never with a cookie the browser attaches on its own, so there is nothing for
/// <c>AllowCredentials</c> to permit and turning it on would only widen what a compromised origin
/// could do. The SignalR hub is covered by the same policy and carries its token in the query
/// string (see <c>Program.cs</c>).
/// </para>
/// <para>
/// <strong>Nothing configured means no origin matches</strong>, so no CORS header is written and a
/// browser blocks the call exactly as it would if this feature did not exist. That is the right
/// default for the demo and for a deployment serving its client from this same origin.
/// </para>
/// <para>
/// The policy is built from <see cref="IConfiguration"/> at resolve time rather than at
/// registration: configuration read while the container is being built is read before a host's own
/// sources are applied, which would make an override invisible. That trap is why the middleware is
/// always added and the policy is always registered — the "is it configured?" question is answered
/// once, lazily, where the answer is complete.
/// </para>
/// </remarks>
public static class ClientCors
{
    /// <summary>The configuration key holding the allowed origins.</summary>
    public const string OriginsKey = "Cors:Origins";

    /// <summary>The policy name the pipeline applies.</summary>
    public const string PolicyName = "client";

    /// <summary>Registers the policy, built from configuration when it is first needed.</summary>
    /// <param name="services">The host's service collection.</param>
    public static IServiceCollection AddClientCors(this IServiceCollection services)
    {
        services.AddCors();
        services
            .AddOptions<CorsOptions>()
            .Configure<IConfiguration>((cors, configuration) => cors.AddPolicy(
                PolicyName,
                policy => policy
                    .WithOrigins(configuration.GetSection(OriginsKey).Get<string[]>() ?? [])
                    .AllowAnyHeader()
                    .AllowAnyMethod()

                    // So a browser stops asking on every request for a board that polls.
                    .SetPreflightMaxAge(TimeSpan.FromHours(1))));

        return services;
    }
}
