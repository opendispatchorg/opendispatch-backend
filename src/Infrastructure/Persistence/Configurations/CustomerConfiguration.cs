using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Infrastructure.Persistence.Conversions;

namespace OpenDispatch.Infrastructure.Persistence.Configurations;

/// <summary>
/// Customers, and the service locations they own.
/// </summary>
/// <remarks>
/// The locations are an owned collection, which is what makes them part of this aggregate rather
/// than a table beside it: they are loaded with the customer, saved with the customer, and
/// deleted with the customer, and nothing can reach one without going through its owner.
/// </remarks>
internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(customer => customer.Id);

        builder.HasContactInfo(customer => customer.Contact);

        builder.OwnsMany(customer => customer.Locations, locations =>
        {
            locations.ToTable("service_locations");

            // Its own identity, not the owner-plus-ordinal key EF would invent: a Job points at
            // a ServiceLocationId from outside the aggregate, so the id has to be the key and
            // has to survive the row moving around in the collection.
            locations.HasKey(location => location.Id);
            locations.WithOwner().HasForeignKey("CustomerId");
        });

        builder.HasIndex(customer => customer.OrgId);
    }
}
