using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Domain.Events;

/// <summary>
/// A job has a stop for the first time: it was demand, and now it is on somebody's day.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="AssignmentChanged"/> even though the board treats the two identically,
/// because they are different facts and only one of them can happen to a given stop. A subscriber
/// that cares about the *arrival* of work — a technician's push notification, a customer's "you are
/// booked" message — needs the one that fires once; a subscriber that cares about the plan moving
/// needs the other. Collapsing them into one event would make the first kind impossible to write
/// without a flag, and a flag on an event is a second event wearing a disguise.
/// </para>
/// <para>
/// Raised by the factory rather than by the slice that calls it, which is a reversal of what step 8
/// chose ("an assignment coming into existence is announced by the slice that made it"). Two slices
/// create stops — a dispatcher's drag and the optimiser — and neither announced one, so
/// <em>optimising a day repainted nobody's board</em>. An aggregate that announces its own creation
/// cannot be forgotten by the third slice that plans work.
/// </para>
/// </remarks>
/// <param name="AssignmentId">The stop that now exists.</param>
/// <param name="JobId">The job being planned, carried so a subscriber can act without a lookup.</param>
/// <param name="TechnicianId">Whose day it landed on.</param>
public sealed record AssignmentPlanned(AssignmentId AssignmentId, JobId JobId, TechnicianId TechnicianId)
    : IDomainEvent
{
    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
