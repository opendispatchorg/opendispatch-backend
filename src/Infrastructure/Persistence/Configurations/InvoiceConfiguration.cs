using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Domain.Invoices;

namespace OpenDispatch.Infrastructure.Persistence.Configurations;

/// <summary>
/// Invoices, and the lines they own.
/// </summary>
internal sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");
        builder.HasKey(invoice => invoice.Id);

        // Totals are computed from the lines every time they are asked for, and storing them
        // would create a second answer that can disagree with the first. That is a domain
        // decision (see Invoice.Total); the mapping simply has to not undo it.
        builder.Ignore(invoice => invoice.Total);

        builder.OwnsMany(invoice => invoice.Lines, lines =>
        {
            lines.ToTable("line_items");
            lines.HasKey(line => line.Id);
            lines.WithOwner().HasForeignKey("InvoiceId");
            lines.Ignore(line => line.LineTotal);
        });

        builder.HasIndex(invoice => invoice.OrgId);
    }
}
