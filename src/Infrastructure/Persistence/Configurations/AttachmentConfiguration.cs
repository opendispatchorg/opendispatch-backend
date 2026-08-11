using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Domain.Attachments;

namespace OpenDispatch.Infrastructure.Persistence.Configurations;

/// <summary>
/// Attachments: what was captured in the field, minus the bytes.
/// </summary>
internal sealed class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("attachments");

        // The device's id, and the primary key: the uniqueness that makes an upload idempotent is
        // the uniqueness a primary key already enforces, so two retries arriving at once cannot
        // both write a row however the application is feeling.
        builder.HasKey(attachment => attachment.Id);

        builder.HasIndex(attachment => attachment.OrgId);

        // What every read of this table asks: the attachments for a job. The technician app shows
        // them against the visit, and the export walks them job by job.
        builder.HasIndex(attachment => attachment.JobId);
    }
}
