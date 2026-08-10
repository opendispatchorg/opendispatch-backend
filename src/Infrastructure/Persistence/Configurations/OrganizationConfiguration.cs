using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Domain.Organizations;

namespace OpenDispatch.Infrastructure.Persistence.Configurations;

/// <summary>
/// Organizations: the tenants.
/// </summary>
/// <remarks>
/// No index on <c>OrgId</c>, unlike every other aggregate — here it is the primary key, and the
/// key's index is the one every org-scoped lookup would have wanted.
/// </remarks>
internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("organizations");
        builder.HasKey(organization => organization.Id);
    }
}
