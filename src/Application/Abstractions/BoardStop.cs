using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// A planned visit, as it appears on a technician's lane.
/// </summary>
/// <remarks>
/// It carries the assignment's identity because dragging this block is what the manual
/// dispatch path acts on, and the job's summary because the block is labelled with the work,
/// not with the plan.
/// </remarks>
/// <param name="AssignmentId">The stop's own identity — what a drag on the board reschedules.</param>
/// <param name="Sequence">Where it falls in the technician's run, counting from zero.</param>
/// <param name="ScheduledStart">
/// When the technician is planned to start work — which is the instant lateness is measured at,
/// and why the block below can be derived rather than flagged.
/// </param>
/// <param name="TravelMin">Minutes of driving to get here from the previous stop.</param>
/// <param name="Job">
/// What the visit is for. Deliberately not late/on-time: whether a stop counts as late is a
/// rule with one right answer, and until the domain states it, the board would be inventing
/// a second one. Everything needed to derive it is here.
/// </param>
public sealed record BoardStop(
    AssignmentId AssignmentId,
    int Sequence,
    DateTimeOffset ScheduledStart,
    double TravelMin,
    BoardJob Job);
