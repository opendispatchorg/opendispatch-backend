namespace OpenDispatch.Api.Board;

/// <summary>
/// What makes the dispatch board work on more than one host.
/// </summary>
/// <remarks>
/// <para>
/// <strong>SignalR keeps its groups in the memory of the process holding the connection.</strong> A
/// dispatcher's browser is connected to one instance; the request that changes a job may be handled
/// by another; and the second instance publishing to "the org's group" reaches only the connections
/// it is itself holding. The dispatcher sees nothing. Nothing errors, nothing logs, and the board is
/// simply no longer live for whoever is on the wrong host — which is the worst shape a defect can
/// take, and the reason this file exists rather than a paragraph in a runbook.
/// </para>
/// <para>
/// A backplane is what makes a group mean the same thing on every instance: each host publishes to
/// Redis and every host delivers to its own connections. <c>SignalR:Redis</c> is a StackExchange
/// connection string; set it and the board survives being scaled, leave it and this registers
/// exactly what was here before.
/// </para>
/// <para>
/// <strong>Unset is a supported configuration, not a broken one</strong> — a single host needs no
/// backplane, and one host is a perfectly ordinary way to run a shop's dispatch system. What was
/// not supported, and now is, is finding that out by scaling to two.
/// </para>
/// <para>
/// The configuration is read here, at registration, because a backplane is a structural choice
/// about the container rather than a per-request one — unlike the rate limiter and CORS, which read
/// their options lazily because a host can change them under a running server. The consequence for
/// tests is real and worth knowing: a value supplied after the container is built (an
/// <c>InMemoryCollection</c> added by <c>WebApplicationFactory</c>) arrives too late to be seen
/// here, which is why <c>ApiFactory</c> also pushes its settings through <c>UseSetting</c>.
/// </para>
/// </remarks>
public static class BoardBackplane
{
    /// <summary>The configuration key naming the Redis instance to coordinate through.</summary>
    public const string ConnectionKey = "SignalR:Redis";

    /// <summary>
    /// The channel prefix every host in one deployment shares, and no other deployment does.
    /// </summary>
    /// <remarks>
    /// Named rather than left to the default so two deployments pointed at one Redis — a staging
    /// and a production that share an instance, which happens — cannot deliver each other's board
    /// events. Tenancy is enforced inside the payloads by the org group; this is the boundary one
    /// level out, between whole systems.
    /// </remarks>
    public const string ChannelPrefix = "opendispatch";

    /// <summary>
    /// Registers the hub, with a Redis backplane when one is configured.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">The host's configuration, read now rather than per request.</param>
    /// <returns>Whether a backplane was configured — the host says so at startup either way.</returns>
    public static bool AddDispatchBoardRealtime(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var signalR = services.AddSignalR();
        var connection = configuration[ConnectionKey];

        if (string.IsNullOrWhiteSpace(connection))
        {
            return false;
        }

        signalR.AddStackExchangeRedis(connection, options =>
            options.Configuration.ChannelPrefix = StackExchange.Redis.RedisChannel.Literal(ChannelPrefix));

        return true;
    }
}
