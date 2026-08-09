using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Domain.Events;

/// <summary>
/// An assignment's plan moved — a different technician, a different time, a different place
/// in the run. One event covers all of it: subscribers care that the plan is no longer what
/// they last saw, not which field did it.
/// </summary>
/// <param name="AssignmentId">The assignment whose plan changed.</param>
/// <param name="JobId">The job being planned, carried so the dispatch board can update the row without a lookup.</param>
/// <param name="TechnicianId">Who the work now belongs to, after the change.</param>
public sealed record AssignmentChanged(AssignmentId AssignmentId, JobId JobId, TechnicianId TechnicianId)
    : IDomainEvent
{
    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
