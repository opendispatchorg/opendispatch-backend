using Microsoft.EntityFrameworkCore;
using OpenDispatch.Domain.Common;
using OpenDispatch.Infrastructure.Persistence;

namespace OpenDispatch.Infrastructure.Events;

/// <summary>
/// Forgets the outbox rows for events that have just been delivered in-process.
/// </summary>
/// <remarks>
/// <para>
/// Called by whoever holds the context at the moment of delivery — the transaction wrapper after a
/// commit, and the interceptor for a save that had no transaction around it. Deliberately not by
/// <see cref="DomainEventDispatcher"/>: a dispatcher that took an <c>AppDbContext</c> closes a
/// cycle (the context builds the interceptor that builds the dispatcher) which recurses at
/// construction and hangs the host with no output at all.
/// </para>
/// <para>
/// Matched on what the rows carry — the owning organization, the event's type and the instant it
/// describes — because nothing in memory holds the row's id: the interceptor wrote them as part of
/// a save nobody kept a handle to. Two events of one type at one instant are the same fact twice,
/// so deleting both is right.
/// </para>
/// <para>
/// <strong>Scoped to the tenant and matched on the exact instant, both of which it once was
/// not.</strong> The predicate used to be type plus <c>OccurredAt &gt;= oldest</c> against a table
/// with no query filter — so one organization's successful publish deleted every row of that type
/// at or after that moment across the whole deployment, including another organization's rows whose
/// own publish had just failed and which the sweep was supposed to redeliver. Silent, cross-tenant,
/// and exactly the loss this table exists to prevent. The prose above only ever justified equality;
/// the inequality was never argued for.
/// </para>
/// <para>
/// A failure here is not fatal and is not caught: the rows stay, the sweep delivers them again, and
/// the subscribers are idempotent. That is the trade the outbox exists to make.
/// </para>
/// </remarks>
internal static class OutboxTrail
{
    /// <summary>Deletes the rows for <paramref name="delivered"/>.</summary>
    public static async Task ForgetAsync(
        AppDbContext context,
        IReadOnlyList<IDomainEvent> delivered,
        CancellationToken ct)
    {
        if (delivered.Count == 0)
        {
            return;
        }

        var types = delivered.Select(domainEvent => domainEvent.GetType().FullName!).Distinct().ToList();
        var instants = delivered.Select(domainEvent => domainEvent.OccurredAt).Distinct().ToList();
        var owner = context.CurrentOrgId;

        await context.Outbox
            .Where(message =>
                message.OrgId == owner
                && types.Contains(message.Type)
                && instants.Contains(message.OccurredAt))
            .ExecuteDeleteAsync(ct)
            .ConfigureAwait(false);
    }
}
