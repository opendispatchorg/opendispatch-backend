using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Infrastructure.Events;

namespace OpenDispatch.Infrastructure.Persistence.Configurations;

/// <summary>
/// The outbox: domain events written in the transaction that raised them.
/// </summary>
/// <remarks>
/// The third configuration here whose type is not a domain aggregate, and the least business-like
/// of them — this is the delivery mechanism's own record. It has no <c>OrgId</c>, deliberately: a
/// message is claimed and delivered by a background dispatcher that serves the whole deployment
/// rather than one tenant, and a column the tenant filter would scope by would leave it unable to
/// read anything (nothing resolves a tenant outside a request).
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
    }
}
