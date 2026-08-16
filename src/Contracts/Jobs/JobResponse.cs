namespace OpenDispatch.Contracts.Jobs;

/// <summary>One job, as the clients see it: the demand, and nothing about the plan.</summary>
/// <param name="Id">Its identity.</param>
/// <param name="CustomerId">Whose work it is.</param>
/// <param name="LocationId">Which of that customer's service locations it happens at.</param>
/// <param name="Latitude">Where it is, in decimal degrees.</param>
/// <param name="Longitude">Where it is, in decimal degrees.</param>
/// <param name="RequiredSkill">The skill a technician must have to take it.</param>
/// <param name="Priority">How badly it needs doing.</param>
/// <param name="WindowStart">When the promised window opens.</param>
/// <param name="WindowEnd">When the promised window closes.</param>
/// <param name="EstimatedDuration">How long the work should take once a technician is on site.</param>
/// <param name="Status">How far through its life the job is.</param>
/// <param name="Notes">What a technician wrote about it, or <see langword="null"/> if nobody has.</param>
/// <param name="ErasedAt">
/// When this job's customer was erased, or <see langword="null"/> if they were not. A phone reading
/// this knows the job is finished business and that nothing more may be written about it.
/// </param>
public sealed record JobResponse(
    Guid Id,
    Guid CustomerId,
    Guid LocationId,
    double Latitude,
    double Longitude,
    string RequiredSkill,
    JobPriority Priority,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    TimeSpan EstimatedDuration,
    JobStatus Status,
    string? Notes,
    DateTimeOffset? ErasedAt);
