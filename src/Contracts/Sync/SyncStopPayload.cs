namespace OpenDispatch.Contracts.Sync;

/// <summary>
/// A planned stop, inside a <see cref="SyncChange"/> whose <c>Entity</c> is
/// <c>"assignment"</c> — the same name a removed stop uses, so a client groups both under one
/// vocabulary rather than switching between "stop" and "assignment" depending on whether the row
/// still exists.
/// </summary>
/// <remarks>
/// The assignment's own id and version are not repeated here, for the same reason
/// <see cref="SyncJobPayload"/> omits <c>JobId</c>/<c>Version</c> — they are already on the
/// <see cref="SyncChange"/> envelope.
/// </remarks>
/// <param name="JobId">The work it is for. The device joins this to a <see cref="SyncJobPayload"/>.</param>
/// <param name="Sequence">Where it falls in the day, counting from zero.</param>
/// <param name="ScheduledStart">When the technician is planned to start work.</param>
/// <param name="TravelMin">Minutes of driving to get here from the previous stop.</param>
public sealed record SyncStopPayload(Guid JobId, int Sequence, DateTimeOffset ScheduledStart, double TravelMin);
