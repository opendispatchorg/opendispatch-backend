using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

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
/// <param name="ErasedAt">
/// When this job's customer was erased, or <see langword="null"/> if they were not — which is what
/// explains a job with no notes sitting at Null Island.
/// </param>
/// <param name="Lines">
/// What the work has taken — the labour and parts the technician recorded on site, in the order
/// they were recorded. Empty for a job nobody has worked yet.
/// </param>
/// <remarks>
/// <para>
/// One shape for both <c>GetJobQuery</c> and <c>ListJobsQuery</c>, the treatment
/// <c>TechnicianSummary</c> gets rather than the two-shape split <c>Customer</c> needs.
/// <see cref="Notes"/> and <see cref="Lines"/> are the two fields a list arguably does not need,
/// and they cost a string and a short collection per row rather than a second record type to save
/// them. The lines are the only unbounded member, and a job's is bounded in practice by what one
/// technician can write down during one visit — while leaving them out is how the office ends up
/// unable to see what it is about to bill, on the very screen where it decides to bill it.
/// </para>
/// <para>
/// They are read for free. <c>JobLine</c> is an EF owned collection, so it is already materialised
/// with every job this system loads — <c>GET /jobs/{id}</c>, <c>GET /jobs</c> and <c>GET /export</c>
/// were each carrying the lines and discarding them at the projection.
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
    string? Notes,
    DateTimeOffset? ErasedAt,
    IReadOnlyList<JobLineSummary> Lines);

/// <summary>One thing the work took, as a reader sees it.</summary>
/// <param name="Id">The line's identity within its job.</param>
/// <param name="Kind">Labour or a part.</param>
/// <param name="Description">What the technician wrote.</param>
/// <param name="Quantity">How many — hours for labour, units for parts.</param>
/// <param name="UnitPrice">What one costs.</param>
/// <remarks>
/// The same five fields <c>/sync/pull</c> already publishes for a job's lines, deliberately: a
/// technician's phone and a dispatcher's browser are looking at the same thing, and two shapes for
/// it would be two things to keep in step. <c>RecordedAt</c> is not among them for the same reason
/// it is not in the sync payload — it is a device's clock, kept as evidence rather than published
/// as fact.
/// </remarks>
public sealed record JobLineSummary(
    JobLineId Id,
    LineItemKind Kind,
    string Description,
    decimal Quantity,
    Money UnitPrice);

/// <summary>
/// A job's recorded lines, as the three readers of <see cref="JobSummary"/> all want them.
/// </summary>
/// <remarks>
/// Shared, unlike the surrounding job projections, which each stayed private on the argument that
/// "two short call sites is cheaper to keep than to abstract". There are three of them now and this
/// is a nested loop rather than a field copy — the arithmetic changed, and a fourth reader writing
/// its own would be the one that quietly dropped a field.
/// </remarks>
internal static class JobLineProjection
{
    public static IReadOnlyList<JobLineSummary> Of(Job job) =>
    [
        .. job.Lines.Select(line => new JobLineSummary(
            line.Id, line.Kind, line.Description, line.Quantity, line.UnitPrice)),
    ];
}
