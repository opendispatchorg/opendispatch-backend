namespace OpenDispatch.Contracts.Schedule;

/// <summary>The body of <c>POST /schedule/insert</c>: an emergency, named by the job it is for.</summary>
/// <param name="JobId">The work that has just come in.</param>
public sealed record InsertJobRequest(Guid JobId);
