namespace OpenDispatch.Contracts.Jobs;

/// <summary>The body of <c>POST /jobs/{id}/assign</c>.</summary>
/// <param name="TechnicianId">Whose day it goes on.</param>
/// <param name="ScheduledStart">When they are planned to arrive.</param>
public sealed record AssignJobRequest(Guid TechnicianId, DateTimeOffset ScheduledStart);
