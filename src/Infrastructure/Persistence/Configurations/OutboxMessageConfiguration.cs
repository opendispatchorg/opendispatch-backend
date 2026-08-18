using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Infrastructure.Events;

namespace OpenDispatch.Infrastructure.Persistence.Configurations;

/// <summary>
/// The outbox: domain events written in the transaction that raised them.
/// </summary>
/// <remarks>
/// <para>
/// The third configuration here whose type is not a domain aggregate, and the least business-like
/// of them — this is the delivery mechanism's own record.
/// </para>
/// <para>
/// <strong>It carries an <c>OrgId</c>, and the reasoning that once said it should not was half
/// right.</strong> The original argument — that a dispatcher serving the whole deployment cannot
/// read a table the tenant filter scopes, because nothing resolves a tenant outside a request — is
/// true, and is why the column is <em>nullable</em> and no filter is applied to it. What it missed
/// is the other half: every subscriber the sweep publishes to <em>is</em> tenant-scoped, so without
/// knowing whose message this is, the dispatcher had nobody to resolve and every delivery threw.
/// The column is what lets it resolve one per claim. See <c>OutboxSweep</c>.
/// </para>
/// </remarks>
internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(message => message.Id);

        // Typed so Postgres validates it and a human can read a stuck message without unescaping
        // a string — the same reasoning the sync op log's payload column carries.
        builder.Property(message => message.Payload).HasColumnType("jsonb");

        // What the dispatcher asks: the oldest messages still here. Ordering by when the thing
        // happened keeps a reaction's delivery in the order the domain produced it.
        builder.HasIndex(message => message.OccurredAt);

        // And what it asks second: whose work is waiting, then that tenant's oldest. The sweep
        // claims per tenant — it has to, because it resolves one before publishing — so this is the
        // index behind every claim it makes.
        builder.HasIndex(message => new { message.OrgId, message.OccurredAt });
    }
}
