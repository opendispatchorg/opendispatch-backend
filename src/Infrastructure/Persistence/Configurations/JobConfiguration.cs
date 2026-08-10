using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Infrastructure.Persistence.Conversions;

namespace OpenDispatch.Infrastructure.Persistence.Configurations;

/// <summary>
/// Jobs: the demand.
/// </summary>
/// <remarks>
/// <c>CustomerId</c> and <c>LocationId</c> are ids, not navigations, and get no foreign key —
/// aggregates reference each other by id (Document 2 §3), and a navigation here would be the
/// thing that lets someone load a customer through a job and mutate two aggregates in one
/// transaction.
/// </remarks>
internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");
        builder.HasKey(job => job.Id);

        // Location is a geography(Point,4326) by the model-wide GeoPoint conversion; the window
        // is two timestamptz columns, window_start and window_end.
        builder.HasTimeWindow(job => job.Window);

        // Every query that is not by primary key is scoped to an organisation, because step 29's
        // global filter puts it there whether the query asked or not.
        builder.HasIndex(job => job.OrgId);
    }
}
