namespace OpenDispatch.Contracts.Jobs;

/// <summary>The body of <c>POST /jobs</c>.</summary>
/// <param name="CustomerId">Whose work it is.</param>
/// <param name="LocationId">Which of that customer's service locations it happens at.</param>
/// <param name="RequiredSkill">The skill a technician must have to take it.</param>
/// <param name="Priority">How badly it needs doing.</param>
/// <param name="WindowStart">When the promised window opens.</param>
/// <param name="WindowEnd">When the promised window closes.</param>
/// <param name="EstimatedDuration">How long the work should take once a technician is on site.</param>
public sealed record CreateJobRequest(
    Guid CustomerId,
    Guid LocationId,
    string RequiredSkill,
    JobPriority Priority,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    TimeSpan EstimatedDuration);
