using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Domain.Technicians;
using OpenDispatch.Infrastructure.Persistence.Conversions;

namespace OpenDispatch.Infrastructure.Persistence.Configurations;

/// <summary>
/// Technicians: the crew.
/// </summary>
internal sealed class TechnicianConfiguration : IEntityTypeConfiguration<Technician>
{
    public void Configure(EntityTypeBuilder<Technician> builder)
    {
        builder.ToTable("technicians");
        builder.HasKey(technician => technician.Id);

        builder.HasTimeWindow(technician => technician.Shift);

        // Written through the backing field rather than the property: Skills is a read-only
        // view, and the set that comes back from the converter is a whole new one.
        builder.Property(technician => technician.Skills)
            .HasConversion(new SkillSetConverter(), new SkillSetComparer())
            .HasField("_skills")
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(technician => technician.OrgId);
    }
}
