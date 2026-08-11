using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// A job as the technician's device should now hold it.
/// </summary>
/// <remarks>
/// <para>
/// A whole state rather than a diff, which is the entire conflict policy in one shape: the server's
/// copy is what is true, and a device rebases by replacing rather than merging. A phone that has
/// been offline for a day has no useful base to apply a diff to.
/// </para>
/// <para>
/// It carries what the field needs and not what the office does: where to go, what was promised,
/// what the work is, and what has been recorded against it so far. There is no assignment here —
/// the plan is a <see cref="SyncStopState"/>, because a job can be re-planned without changing and
/// re-planning is the thing that happens most.
/// </para>
/// </remarks>
/// <param name="JobId">Which job.</param>
/// <param name="Status">How far through its life it is — which buttons the app offers.</param>
/// <param name="Priority">How badly it needs doing.</param>
/// <param name="RequiredSkill">What it takes to do it.</param>
/// <param name="Window">What was promised the customer.</param>
/// <param name="EstimatedDuration">How long the work should take.</param>
/// <param name="Location">Where it is, for navigation.</param>
/// <param name="CustomerName">Who it is for.</param>
/// <param name="Address">Where it is, for a human — neither lives on <c>Job</c>, which is why this is a projection.</param>
/// <param name="Notes">What has been written about it, or <see langword="null"/> if nothing has.</param>
/// <param name="NotesRecordedAt">
/// When the notes were written, by the clock of whoever wrote them. The device needs it to know
/// whether its own unsent note would win: the same comparison the server will make.
/// </param>
/// <param name="Lines">What the work has taken so far, in the order it was recorded.</param>
/// <param name="Version">
/// The job's concurrency stamp as the server holds it, and what the device puts in the next
/// operation it bases on this. The two halves of sync close here: what came back from a pull is
/// what a later push is judged against.
/// </param>
public sealed record SyncJobState(
    JobId JobId,
    JobStatus Status,
    JobPriority Priority,
    string RequiredSkill,
    TimeWindow Window,
    TimeSpan EstimatedDuration,
    GeoPoint Location,
    string CustomerName,
    string Address,
    string? Notes,
    DateTimeOffset? NotesRecordedAt,
    IReadOnlyList<SyncJobLine> Lines,
    long Version);

/// <summary>One line of what the work took, as the device should hold it.</summary>
/// <param name="Id">The line's identity within its job.</param>
/// <param name="Kind">Labour or a part.</param>
/// <param name="Description">What it was.</param>
/// <param name="Quantity">How many.</param>
/// <param name="UnitPrice">What one costs.</param>
/// <remarks>
/// Sent back so a device can tell what the server has from what it has queued: a line it recorded
/// offline appears here once the push that carried it has landed.
/// </remarks>
public sealed record SyncJobLine(
    JobLineId Id,
    LineItemKind Kind,
    string Description,
    decimal Quantity,
    Money UnitPrice);
