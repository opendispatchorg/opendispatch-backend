using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Application.Sync;

namespace OpenDispatch.Infrastructure.Persistence.Configurations;

/// <summary>
/// The notes saying something is gone.
/// </summary>
/// <remarks>
/// Keyed by the removed thing's own id, because a thing is removed once — which also means a
/// second attempt to record the same removal is refused by the database rather than by anybody
/// remembering to look.
/// </remarks>
internal sealed class SyncRemovalConfiguration : IEntityTypeConfiguration<SyncRemoval>
{
    public void Configure(EntityTypeBuilder<SyncRemoval> builder)
    {
        builder.ToTable("sync_removals");
        builder.HasKey(removal => removal.EntityId);

        builder.HasIndex(removal => removal.OrgId);

        // What pull asks: this technician's removals since a cursor. The change stamp is not on the
        // model as a member, so the index over it is written by hand in the migration beside the
        // trigger that maintains it.
        builder.HasIndex(removal => removal.TechnicianId);
    }
}
