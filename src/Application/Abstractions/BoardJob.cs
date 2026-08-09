using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// A job as the board shows it — enough to draw a block, place a pin, and decide what to do
/// about it.
/// </summary>
/// <remarks>
/// <para>
/// A summary, not a job. It holds no history, no notes and no line items, because a board
/// that fetched those would pay for them on every job in the day to show them on none.
/// </para>
/// <para>
/// <see cref="CustomerName"/> and <see cref="Address"/> are the reason this is a projection
/// and not a list of aggregates: neither lives on <c>Job</c>, which references its customer
/// and its service location by id. Reaching them through the aggregates means a second and
/// third load per job; reaching them through a join costs nothing.
/// </para>
/// </remarks>
/// <param name="JobId">Which job. What a status change or an assign command acts on.</param>
/// <param name="Status">How far through its life it is — what colours the block.</param>
/// <param name="Priority">How badly it needs doing.</param>
/// <param name="RequiredSkill">What a technician needs to take it, so a dispatcher can see why it will not go somewhere.</param>
/// <param name="Window">What was promised the customer, which the block is judged against.</param>
/// <param name="EstimatedDuration">How long the work should take — the width of the block.</param>
/// <param name="Location">Where it is, for the map.</param>
/// <param name="CustomerName">Who it is for.</param>
/// <param name="Address">Where it is, for a human.</param>
public sealed record BoardJob(
    JobId JobId,
    JobStatus Status,
    JobPriority Priority,
    string RequiredSkill,
    TimeWindow Window,
    TimeSpan EstimatedDuration,
    GeoPoint Location,
    string CustomerName,
    string Address);
