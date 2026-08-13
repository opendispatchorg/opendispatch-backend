namespace OpenDispatch.Contracts.Technicians;

/// <summary>The body of <c>PUT /technicians/{id}/shift</c>.</summary>
/// <param name="Start">When their working hours open.</param>
/// <param name="End">When their working hours close.</param>
public sealed record SetShiftRequest(DateTimeOffset Start, DateTimeOffset End);
