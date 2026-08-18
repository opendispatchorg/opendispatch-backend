using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Infrastructure.Persistence;

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

    /// <summary>
    /// One sweep per organization with work waiting, each in a scope that has resolved it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>This is the fix for the defect that made the outbox inert.</strong> It used to open
    /// one bare scope and call the sweep. Nothing resolves a tenant outside a request, so
    /// <c>ITenantContext.OrgId</c> threw on the first subscriber that read it — which is all of
    /// them — the message was marked failed, and the row stayed. Every message the sweep ever
    /// touched became a poison row, <c>outbox_messages</c> grew without bound, and the runbook's
    /// alert on that table would have fired permanently and never cleared. The net that exists so a
    /// cancelled job's stop cannot stay on a technician's phone caught nothing.
    /// </para>
    /// <para>
    /// It went unnoticed because the ordinary path — publishing in-process after a commit — has a
    /// resolved tenant and works, and deletes its rows promptly. The outbox is only reached when
    /// that path failed, so the safety net was broken precisely in the situation it exists for.
    /// </para>
    /// <para>
    /// Asking which tenants have work first costs one small query per sweep and keeps the claim,
    /// the lock and the publish inside one transaction belonging to one organization — rather than
    /// claiming across tenants and publishing under whichever identity happened to be resolved.
    /// </para>
    /// </remarks>
    internal async Task SweepAsync(CancellationToken ct)
    {
        foreach (var owner in await WaitingAsync(ct).ConfigureAwait(false))
        {
            await using var scope = scopes.CreateAsyncScope();

            scope.ServiceProvider.GetRequiredService<ITenantScope>().Resolve(owner);

            await scope.ServiceProvider.GetRequiredService<OutboxSweep>()
                .DeliverPendingAsync(options.Grace, options.BatchSize, ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The organizations with undelivered messages old enough to sweep.
    /// </summary>
    /// <remarks>
    /// Read in a scope of its own, with no tenant resolved, which is exactly what the outbox table
    /// carrying no query filter is for: finding whose work is waiting has to be possible before
    /// anybody has been resolved. Rows with no owner are the ones written before the column existed
    /// — they are named in the log rather than guessed at, because there is no honest way to say
    /// whose they were.
    /// </remarks>
    private async Task<IReadOnlyList<OrgId>> WaitingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();

        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var older = clock.UtcNow - options.Grace;

        var pending = await context.Outbox
            .Where(message => message.OccurredAt < older)
            .Select(message => message.OrgId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var ownerless = pending.Count(owner => owner is null);

        if (ownerless > 0)
        {
            OutboxLog.Ownerless(log, ownerless);
        }

        return [.. pending.Where(owner => owner is not null).Select(owner => owner!.Value)];
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
    /// Rows written before the outbox recorded which organization a message belongs to. They cannot
    /// be delivered — there is nobody to resolve — and they are not deleted, because a migration
    /// that threw away undelivered reactions would be the same class of silent loss the table
    /// exists to prevent. An operator clears them once satisfied nothing is owed; the runbook says
    /// how.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "{Count} outbox message(s) predate tenant-aware delivery and cannot be swept. See "
            + "the runbook: they need clearing by hand once you are satisfied nothing is owed.")]
    internal static partial void Ownerless(ILogger logger, int count);

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
