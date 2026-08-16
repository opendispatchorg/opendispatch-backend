using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Application.Auditing;

namespace OpenDispatch.Infrastructure.Persistence.Configurations;

/// <summary>
/// The audit trail: who did what, when.
/// </summary>
/// <remarks>
/// Tenant-scoped like the business tables — an audit trail is a shop's own record of its own
/// people, and one organization has no business reading another's. That falls out of the sweep
/// rather than being declared here, because the entry carries an <c>OrgId</c>.
/// </remarks>
internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(entry => entry.Id);

        // What every read of this table asks: what happened here, most recent first.
        builder.HasIndex(entry => new { entry.OrgId, entry.At });

        // Typed, so a target can be queried into — "everything anybody did to job X" is a jsonb
        // containment query rather than a string search.
        builder.Property(entry => entry.Targets).HasColumnType("jsonb");
    }
}
