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
/// EF builds and validates the whole model the first time anything touches the context, and a
/// <see cref="DbSet{TEntity}"/> puts its entity type into that model. Step 25's converters and
/// complex types are enough for <c>Job</c>, <c>Assignment</c> and <c>Organization</c> — every
/// constructor parameter now binds to something mappable — but not for the other three, whose
/// remaining members need decisions that belong to step 26: <c>Technician</c>'s
/// <c>IEnumerable&lt;string&gt; skills</c> parameter, <c>Customer</c>'s <c>ContactInfo</c> and
/// owned locations, <c>Invoice</c>'s owned lines and computed total.
/// </para>
/// <para>
/// All six stay out, rather than the three that could now go in. Letting those three map by
/// convention would mint table, key and column names that step 26 immediately changes, and would
/// leave the next person to work out which roots were real and which were provisional. Excluded,
/// a set throws the moment anything reaches for one, which is the failure worth having while the
/// mappings are still being written.
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
