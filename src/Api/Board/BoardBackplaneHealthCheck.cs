using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace OpenDispatch.Api.Board;

/// <summary>
/// Whether the dispatch board's backplane is answering.
/// </summary>
/// <remarks>
/// <para>
/// Registered only when <c>SignalR:Redis</c> is configured, because a deployment without a
/// backplane is a supported one and a check that reported "no Redis" as a problem would page
/// somebody about a decision they made. With one configured, though, Redis is load-bearing: the
/// whole reason it is there is that the board is wrong without it across more than one instance,
/// and it was the one configured dependency <c>/health/ready</c> never asked about.
/// </para>
/// <para>
/// <strong>Degraded, not unhealthy</strong>, for the reason the attachment store gives: a shop with
/// a dead backplane can still dispatch, invoice and sync — what it loses is the board updating
/// across instances, which is a capability rather than the system. The report names it.
/// </para>
/// <para>
/// Its own multiplexer, connected lazily and shared. SignalR's is not reachable from here, and one
/// extra connection per host is a smaller price than either reaching into SignalR's internals or
/// opening a connection per probe. <c>AbortOnConnectFail</c> is off so a Redis that is down at
/// startup produces a failed probe rather than a host that will not boot — the backplane is not
/// something to refuse to serve over.
/// </para>
/// </remarks>
public sealed class BoardBackplaneHealthCheck : IHealthCheck, IDisposable
{
    /// <summary>The name this check is registered and reported under.</summary>
    public const string Name = "board-backplane";

    private readonly Lazy<Task<IConnectionMultiplexer>> _connection;

    /// <summary>Creates the check over a Redis connection string.</summary>
    /// <param name="connectionString">The same one the backplane uses.</param>
    public BoardBackplaneHealthCheck(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;

        _connection = new Lazy<Task<IConnectionMultiplexer>>(
            () => ConnectionMultiplexer.ConnectAsync(options).ContinueWith(
                connected => (IConnectionMultiplexer)connected.Result,
                TaskScheduler.Default));
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = await _connection.Value.ConfigureAwait(false);

            // PING against the whole configuration rather than a single endpoint, so a cluster with
            // one node down is not reported as a dead backplane.
            await connection.GetDatabase().PingAsync().ConfigureAwait(false);

            return HealthCheckResult.Healthy("The board backplane is reachable.");
        }
        catch (Exception unreachable) when (unreachable is not OperationCanceledException)
        {
            return HealthCheckResult.Degraded(
                "The board backplane cannot be reached; the live board will not update across instances.",
                unreachable);
        }
    }

    public void Dispose()
    {
        if (_connection.IsValueCreated && _connection.Value is { IsCompletedSuccessfully: true } connected)
        {
            connected.Result.Dispose();
        }
    }
}
