namespace OpenDispatch.Contracts.Board;

/// <summary>A job as the board shows it, over REST — the snapshot half of <see cref="JobUpdated"/>'s delta.</summary>
/// <param name="JobId">Which job.</param>
/// <param name="Status">How far through its life it is — what colours the block.</param>
/// <param name="Priority">How badly it needs doing.</param>
/// <param name="RequiredSkill">What a technician needs to take it.</param>
/// <param name="WindowStart">When the promised window opens.</param>
/// <param name="WindowEnd">When the promised window closes.</param>
/// <param name="EstimatedDuration">How long the work should take — the width of the block.</param>
/// <param name="Latitude">Where it is, in decimal degrees.</param>
/// <param name="Longitude">Where it is, in decimal degrees.</param>
/// <param name="CustomerName">Who it is for.</param>
/// <param name="Address">Where it is, for a human.</param>
public sealed record BoardJobResponse(
    Guid JobId,
    JobStatus Status,
    JobPriority Priority,
    string RequiredSkill,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    TimeSpan EstimatedDuration,
    double Latitude,
    double Longitude,
    string CustomerName,
    string Address);
