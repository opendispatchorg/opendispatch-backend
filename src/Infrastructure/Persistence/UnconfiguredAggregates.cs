using Microsoft.EntityFrameworkCore;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.Organizations;
using OpenDispatch.Domain.Technicians;

namespace OpenDispatch.Infrastructure.Persistence;

/// <summary>
/// Keeps the six aggregate roots out of the model until their configurations exist.
/// </summary>
/// <remarks>
/// <para>
/// TEMPORARY: removed in step 26, along with the call in <see cref="AppDbContext"/>.
/// </para>
/// <para>
/// EF builds and validates the whole model the first time anything touches the context —
/// including <c>Database.CanConnect()</c>, which is what this step is judged by — and a
/// <see cref="DbSet{TEntity}"/> puts its entity type into that model. These aggregates are not
/// mappable yet: they have no parameterless constructor, so every constructor parameter must bind
/// to a mapped property, and the typed ids and value objects those parameters are made of have no
/// converters until step 25 and no per-aggregate configuration until step 26.
/// </para>
/// <para>
/// So they are excluded rather than half-mapped. Guessing at a shape here would mean step 26
/// unpicking a mapping decision this step had no business making, and a keyless stand-in would
/// quietly query a table that does not exist. Excluded, the sets throw if anything reaches for one
/// before step 26 gives it a real configuration, which is the failure worth having.
/// </para>
/// </remarks>
internal static class UnconfiguredAggregates
{
    internal static void ExcludeUntilConfigured(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<Job>();
        modelBuilder.Ignore<Assignment>();
        modelBuilder.Ignore<Technician>();
        modelBuilder.Ignore<Customer>();
        modelBuilder.Ignore<Invoice>();
        modelBuilder.Ignore<Organization>();
    }
}
