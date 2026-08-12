namespace OpenDispatch.Contracts.Board;

/// <summary>A planned visit, as it appears on a technician's lane over REST.</summary>
/// <param name="AssignmentId">The stop's own identity — what a drag on the board reschedules.</param>
/// <param name="Sequence">Where it falls in the technician's run, counting from zero.</param>
/// <param name="ScheduledStart">When the technician is planned to start work.</param>
/// <param name="TravelMin">Minutes of driving to get here from the previous stop.</param>
/// <param name="LateBy">How far past the promised window the work is planned to begin, or zero when it begins inside it.</param>
/// <param name="Job">What the visit is for.</param>
public sealed record BoardStopResponse(
    Guid AssignmentId,
    int Sequence,
    DateTimeOffset ScheduledStart,
    double TravelMin,
    TimeSpan LateBy,
    BoardJobResponse Job);
