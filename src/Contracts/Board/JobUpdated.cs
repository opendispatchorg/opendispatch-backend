namespace OpenDispatch.Contracts.Board;

/// <summary>
/// A job moved through its lifecycle. Pushed to the org's board group under
/// <see cref="BoardEvents.JobUpdated"/>.
/// </summary>
/// <remarks>
/// <para>
/// Small on purpose (Document 2 §9): ids plus what changed, applied by the client to the
/// board it already holds. Status is what a board actually re-renders minute to minute — it
/// colours the block — and it is the one thing about a job that changes while a dispatcher is
/// watching. A job whose promised window or required skill was edited is a rarer thing that
/// happens in a form, and the board reloading is a fair price for keeping this event from
/// growing into a copy of the projection.
/// </para>
/// <para>
/// Where the job sits in somebody's day is not here. That is the plan, and the plan is
/// <c>AssignmentUpdated</c> — the two are separate aggregates and separate events for the
/// same reason.
/// </para>
/// </remarks>
/// <param name="JobId">Which job the client should apply this to.</param>
/// <param name="Status">What the job's status now is.</param>
/// <param name="Version">
/// The job's concurrency stamp after the change. Two dispatchers acting at once can have
/// their events arrive out of order; a client that keeps the highest version it has seen
/// never applies the older of the two.
/// </param>
public sealed record JobUpdated(Guid JobId, JobStatus Status, long Version);
