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

        // What the work took, as one jsonb column on the job rather than a table beside it.
        //
        // Two reasons, and the second is the load-bearing one. They are never read without the
        // job and never queried on their own — an invoice raised from the work reads the whole
        // job — so a table would buy joins nobody makes. And being part of the job's own row is
        // what makes a recorded line a change *to the job*: the version stamp moves and the sync
        // change stamp moves, so a phone that added a part is told about it by the same mechanism
        // that tells it about a status. A separate table would leave both untouched, because
        // adding a child does not modify its parent's row.
        builder.OwnsMany(job => job.Lines, lines =>
        {
            lines.ToJson("lines");
            lines.Ignore(line => line.LineTotal);
        });

        // Every query that is not by primary key is scoped to an organisation, because step 29's
        // global filter puts it there whether the query asked or not.
        builder.HasIndex(job => job.OrgId);

        // GiST, because a geography column answers "near", "within" and "along" rather than
        // "equals", and a B-tree cannot help with any of them.
        builder.HasIndex(job => job.Location).HasMethod("gist");

        // The dispatch board's index — jobs(org_id, status, window_start) — is not here. EF
        // cannot express an index over a complex type's member, so it is written by hand in the
        // initial migration; see the comment there.
    }
}
