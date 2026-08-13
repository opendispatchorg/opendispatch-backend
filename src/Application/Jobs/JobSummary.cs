using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Application.Jobs;

/// <summary>
/// A job as a reader sees it — everything about the demand, and nothing about the plan.
/// </summary>
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
/// <remarks>
/// <para>
/// One shape for both <c>GetJobQuery</c> and <c>ListJobsQuery</c>, the treatment
/// <c>TechnicianSummary</c> gets rather than the two-shape split <c>Customer</c> needs: a job's
/// own fields are fixed in number, with no unbounded collection like a customer's locations
/// growing the payload. <see cref="Notes"/> is the one field a list arguably does not need, and
/// it costs one string per row rather than a second record type to save it.
/// </para>
/// <para>
/// It carries no assignment — which technician, what time, what sequence. That is the plan, a
/// separate aggregate (Document 2 §3), and reading it is the dispatch board's job (step 48), not
/// this one's.
/// </para>
/// </remarks>
public sealed record JobSummary(
    JobId Id,
    CustomerId CustomerId,
    ServiceLocationId LocationId,
    double Latitude,
    double Longitude,
    string RequiredSkill,
    JobPriority Priority,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    TimeSpan EstimatedDuration,
    JobStatus Status,
    string? Notes);
