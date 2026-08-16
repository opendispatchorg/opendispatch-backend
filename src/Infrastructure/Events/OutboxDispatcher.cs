using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OpenDispatch.Infrastructure.Events;

/// <summary>
/// How much of the outbox a sweep takes, and how long it waits before deciding a message was lost.
/// </summary>
/// <param name="Enabled">Whether the sweep runs at all. Off is for a test suite, not a deployment.</param>
/// <param name="Interval">How often to look.</param>
/// <param name="GracePeriod">
/// How long a message is left alone before it is treated as undelivered. It must be longer than a
/// request takes, because the ordinary path publishes and deletes the row a moment after the commit
/// — sweeping sooner would deliver everything twice for no reason.
/// </param>
/// <param name="BatchSize">How many messages one sweep claims.</param>
public sealed record OutboxOptions(
    bool Enabled = true,
    TimeSpan? Interval = null,
    TimeSpan? GracePeriod = null,
    int BatchSize = 50)
{
    /// <summary>How often the sweep looks, when nothing says otherwise.</summary>
    public TimeSpan Sweep => Interval ?? TimeSpan.FromSeconds(10);

    /// <summary>How old an undelivered message must be before it is picked up.</summary>
    public TimeSpan Grace => GracePeriod ?? TimeSpan.FromSeconds(30);
}

/// <summary>
/// The timer around <see cref="OutboxSweep"/>, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The safety net, not the mechanism. A commit publishes its own events in-process and deletes their
/// rows, so a board is live and a reaction is immediate; this exists for the cases where that never
/// happened — a subscriber that threw, a host killed between committing a job's cancellation and
/// telling anything about it, a deployment that rolled mid-request.
/// </para>
/// <para>
/// The work is a separate, callable class for the reason <c>SyncLogPruner</c> is: a test needs to
/// drive a sweep, not wait for a timer.
/// </para>
/// </remarks>
internal sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    OutboxOptions options,
    ILogger<OutboxDispatcher> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            OutboxLog.Disabled(log);

            return;
        }

        using var timer = new PeriodicTimer(options.Sweep);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception failed)
            {
                // A sweep that cannot run at all — an unreachable database, most likely — must not
                // take the host down with it, and must not stop the sweeps that follow. The next
                // tick tries again; readiness is what reports the database itself.
                OutboxLog.SweepFailed(log, failed);
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }

    /// <summary>One sweep, in a scope of its own.</summary>
    private async Task SweepAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<OutboxSweep>()
            .DeliverPendingAsync(options.Grace, options.BatchSize, ct)
            .ConfigureAwait(false);
    }
}

/// <summary>What the outbox has to say, source-generated.</summary>
internal static partial class OutboxLog
{
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The outbox sweep is disabled. Domain events are delivered in-process only, so a "
            + "reaction lost to a failure or a restart stays lost.")]
    internal static partial void Disabled(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "An outbox sweep could not run.")]
    internal static partial void SweepFailed(ILogger logger, Exception exception);

    /// <remarks>
    /// Information rather than debug: every row this delivers is one the request that raised it
    /// failed to, which is worth a line even when the recovery works.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Delivered {EventType} {MessageId} from the outbox after {Attempts} failed attempt(s).")]
    internal static partial void Delivered(ILogger logger, string eventType, Guid messageId, int attempts);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Could not deliver {EventType} {MessageId}; it has now failed {Attempts} time(s) and "
            + "remains in the outbox.")]
    internal static partial void DeliveryFailed(
        ILogger logger, Exception exception, string eventType, Guid messageId, int attempts);
}
