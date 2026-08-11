using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Application.Sync;

namespace OpenDispatch.Infrastructure.Persistence.Configurations;

/// <summary>
/// The op log: what devices have already done.
/// </summary>
/// <remarks>
/// The one configuration in this folder whose type is not a domain aggregate, because the op log
/// is not one — it is the protocol's own bookkeeping (see <see cref="SyncOpRecord"/>). It is
/// configured here anyway, beside everything else this database holds, rather than in a place a
/// reader would have to be told about.
/// </remarks>
internal sealed class SyncOpConfiguration : IEntityTypeConfiguration<SyncOpRecord>
{
    public void Configure(EntityTypeBuilder<SyncOpRecord> builder)
    {
        builder.ToTable("sync_ops");

        // The device's id, and the primary key: the uniqueness that makes a push idempotent is
        // the same uniqueness a primary key already enforces, so there is no second index and no
        // way for two rows to claim one operation even if the application forgets to look first.
        builder.HasKey(op => op.Id);

        builder.HasIndex(op => op.OrgId);

        // Verbatim, and typed so Postgres validates it and can be queried into later. A text
        // column would take anything, including the truncated body of a request that was cut off
        // mid-upload.
        builder.Property(op => op.Payload).HasColumnType("jsonb");
    }
}
