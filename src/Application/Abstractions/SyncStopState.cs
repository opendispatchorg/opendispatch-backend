using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// A planned visit as the technician's device should now hold it.
/// </summary>
/// <remarks>
/// The plan, kept separate from the demand exactly as the aggregates are: re-optimising a day
/// rewrites stops and touches no job, so a device that has been away usually has more stops to
/// take than jobs.
/// </remarks>
/// <param name="AssignmentId">The stop's own identity, and what a removal names.</param>
/// <param name="JobId">The work it is for. The device joins this to a <see cref="SyncJobState"/>.</param>
/// <param name="Sequence">Where it falls in the day, counting from zero.</param>
/// <param name="ScheduledStart">When the technician is planned to start work.</param>
/// <param name="TravelMin">Minutes of driving to get here from the previous stop.</param>
/// <param name="Version">The stop's concurrency stamp as the server holds it.</param>
public sealed record SyncStopState(
    AssignmentId AssignmentId,
    JobId JobId,
    int Sequence,
    DateTimeOffset ScheduledStart,
    double TravelMin,
    long Version);

/// <summary>
/// Everything that has changed in one technician's world since a cursor.
/// </summary>
/// <remarks>
/// <para>
/// Three lists rather than one heterogeneous stream, because the server knows what kind of thing
/// each change is and a client that had to ask would be parsing its way back to something the
/// server already knew. The wire flattens them (<c>SyncChange</c> carries an entity name), and the
/// flattening is the edge's business.
/// </para>
/// <para>
/// <see cref="RemovedStops"/> is the half that cannot be expressed as a state, and it exists
/// because the alternative is a phone that keeps a stop and drives to it.
/// </para>
/// </remarks>
/// <param name="Jobs">The work, as the server now holds it.</param>
/// <param name="Stops">The plan, as the server now holds it.</param>
/// <param name="RemovedStops">
/// Stops that are no longer this technician's — dropped by a re-optimisation, or handed to
/// somebody else. Ids only: there is no state to send for something that is gone, and a removal
/// for a stop the device never had is a no-op.
/// </param>
public sealed record SyncScopeChanges(
    IReadOnlyList<SyncJobState> Jobs,
    IReadOnlyList<SyncStopState> Stops,
    IReadOnlyList<AssignmentId> RemovedStops);
