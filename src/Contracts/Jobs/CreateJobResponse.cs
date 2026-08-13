namespace OpenDispatch.Contracts.Jobs;

/// <summary>What a successful <c>POST /jobs</c> returns.</summary>
/// <param name="Id">The identity of the job it created.</param>
/// <remarks>
/// A named shape rather than the anonymous <c>{ id }</c> object this route answered with before
/// step 52 — same bytes on the wire (an anonymous type and a record both camelCase to
/// <c>{"id": "..."}</c>), but only a named type gives an OpenAPI document, and therefore
/// <c>@opendispatch/contracts/rest</c>, anything to describe.
/// </remarks>
public sealed record CreateJobResponse(Guid Id);
