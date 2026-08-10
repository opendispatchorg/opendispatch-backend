using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpenDispatch.Domain.Assignments;

namespace OpenDispatch.Infrastructure.Persistence.Configurations;

/// <summary>
/// Assignments: the plan.
/// </summary>
internal sealed class AssignmentConfiguration : IEntityTypeConfiguration<Assignment>
{
    public void Configure(EntityTypeBuilder<Assignment> builder)
    {
        builder.ToTable("assignments");
        builder.HasKey(assignment => assignment.Id);

        builder.HasIndex(assignment => assignment.OrgId);

        // A job is planned once. Neither aggregate can say so — they reference each other by id
        // — so the database is the only place the rule can live, and IAssignmentRepository
        // .GetByJobAsync (step 19) already promises callers it holds. Without the index the
        // symptom is a second stop appearing on the board for the same job.
        builder.HasIndex(assignment => assignment.JobId).IsUnique();
    }
}
