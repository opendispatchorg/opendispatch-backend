namespace OpenDispatch.Contracts.Jobs;

/// <summary>What a successful <c>POST /jobs/{id}/assign</c> returns.</summary>
/// <param name="AssignmentId">The identity of the stop it created or moved — the board has just drawn it.</param>
public sealed record AssignJobResponse(Guid AssignmentId);
