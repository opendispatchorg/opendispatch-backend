namespace OpenDispatch.Contracts.Board;

/// <summary>
/// A stop was planned, moved within a day, or handed to another technician. Pushed to the
/// org's board group under <see cref="BoardEvents.AssignmentUpdated"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the plan changing, which on a board means a block moving between lanes or along
/// one. It carries the whole placement rather than a delta because a drag can change the
/// technician, the time and the sequence at once, and a client applying three partial events
/// in the wrong order draws a lane that never existed.
/// </para>
/// <para>
/// There is no end time: how long a block is comes from the job's estimated duration, which
/// the client already has. Sending a derived value would give the board a second opinion
/// about the same thing.
/// </para>
/// </remarks>
/// <param name="AssignmentId">Which stop.</param>
/// <param name="JobId">The job being planned — the block's contents.</param>
/// <param name="TechnicianId">Whose lane it is now on.</param>
/// <param name="Sequence">Where it falls in that technician's run, counting from zero.</param>
/// <param name="ScheduledStart">When the technician is planned to arrive.</param>
/// <param name="TravelMin">Minutes of driving to reach it from the previous stop — the gap before the block.</param>
/// <param name="Version">
/// The assignment's concurrency stamp after the change, so a client can discard an event
/// older than what it has already applied.
/// </param>
public sealed record AssignmentUpdated(
    Guid AssignmentId,
    Guid JobId,
    Guid TechnicianId,
    int Sequence,
    DateTimeOffset ScheduledStart,
    double TravelMin,
    long Version);
