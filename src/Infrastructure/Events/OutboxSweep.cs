using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Infrastructure.Persistence;

namespace OpenDispatch.Infrastructure.Events;

/// <summary>
/// Delivers the domain events that the request which raised them did not.
/// </summary>
/// <remarks>
/// <para>
/// The work behind <see cref="OutboxDispatcher"/>, in a class that can be called rather than only
/// scheduled — the same split as <c>SyncLogPruner</c> and its verb, and for the same reason: what a
/// test needs to drive is the sweep, not a timer.
/// </para>
/// <para>
/// <strong>Claimed with <c>FOR UPDATE SKIP LOCKED</c></strong>, so a second instance is a second
/// worker rather than a duplicate: each sweep locks the rows it takes for the length of its
/// transaction and another host steps over them. Not needed on one instance, and the difference
/// between scaling being safe and scaling being a surprise.
/// </para>
/// <para>
/// <strong>The reaction commits with the delivery.</strong> A subscriber that writes does so through
/// the same scoped context this transaction belongs to, so the work it does and the disappearance of
/// the message are one commit: a reaction cannot half-happen.
/// </para>
/// <para>
/// <strong>Delivery is at least once.</strong> A message the ordinary path published but did not get
/// to delete is delivered again here, which is why every subscriber in this system is written to
/// tolerate it — withdrawing a stop that is already gone does nothing, and re-pushing a board event
/// repaints a board with what it already shows.
/// </para>
/// </remarks>
public sealed class OutboxSweep(
    AppDbContext context,
    IPublisher publisher,
    IClock clock,
    ILogger<OutboxSweep> log)
{
    /// <summary>
    /// Delivers what is still waiting.
    /// </summary>
    /// <param name="grace">
    /// How long a message is left alone before it is treated as undelivered. It must be longer than
    /// a request takes: the ordinary path deletes its rows a moment after the commit, and sweeping
    /// sooner would deliver everything twice for no reason.
    /// </param>
    /// <param name="batchSize">How many messages this sweep may claim.</param>
    /// <param name="ct">Cancels the sweep.</param>
    /// <returns>How many messages were delivered.</returns>
    public async Task<int> DeliverPendingAsync(TimeSpan grace, int batchSize, CancellationToken ct)
    {
        var older = clock.UtcNow - grace;
        var delivered = 0;

        // The strategy owns the boundary, so the transaction opened inside it is retriable and the
        // saves a subscriber makes are allowed — the arrangement IUnitOfWork.ExecuteInTransactionAsync
        // exists for, here because this is not a request.
        await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            delivered = 0;

            await using var transaction = await context.Database.BeginTransactionAsync(ct).ConfigureAwait(false);

            var claimed = await context.Outbox
                .FromSql($"""
                    SELECT * FROM outbox_messages
                    WHERE occurred_at < {older}
                    ORDER BY occurred_at
                    LIMIT {batchSize}
                    FOR UPDATE SKIP LOCKED
                    """)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var message in claimed)
            {
                if (await DeliverAsync(message, ct).ConfigureAwait(false))
                {
                    delivered++;
                }
            }

            if (claimed.Count > 0)
            {
                await context.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);

        return delivered;
    }

    private async Task<bool> DeliverAsync(OutboxMessage message, CancellationToken ct)
    {
        try
        {
            var domainEvent = DomainEventSerializer.Deserialize(message.Type, message.Payload);

            await publisher.Publish(DomainEventNotifications.Wrap(domainEvent), ct).ConfigureAwait(false);

            context.Outbox.Remove(message);

            OutboxLog.Delivered(log, message.Type, message.Id, message.Attempts);

            return true;
        }
        catch (Exception failed) when (failed is not OperationCanceledException)
        {
            // Left in place, with what went wrong on it. A message that cannot be delivered is a
            // fact somebody has to see rather than a retry loop nobody can find — and the number of
            // rows in this table is the alert that says so.
            message.Failed(failed.ToString(), clock.UtcNow);

            OutboxLog.DeliveryFailed(log, failed, message.Type, message.Id, message.Attempts);

            return false;
        }
    }
}
