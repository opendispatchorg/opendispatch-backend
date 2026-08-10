using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Application.Jobs.CreateJob;

/// <summary>
/// Books a job: what the customer needs doing, where, by when, and how long it should take.
/// </summary>
/// <param name="CustomerId">Whose work it is.</param>
/// <param name="LocationId">Which of that customer's service locations it happens at.</param>
/// <param name="RequiredSkill">The skill a technician must have to take it. A hard scheduling constraint.</param>
/// <param name="Priority">How badly it needs doing — the weight the scheduler pays for leaving it unassigned.</param>
/// <param name="WindowStart">When the promised window opens.</param>
/// <param name="WindowEnd">When the promised window closes. Never earlier than <paramref name="WindowStart"/>.</param>
/// <param name="EstimatedDuration">How long the work should take once the technician is on site.</param>
/// <remarks>
/// <para>
/// This creates <em>demand</em> and nothing else. The job lands <c>Unscheduled</c>: who goes and
/// when is an <c>Assignment</c>, a separate aggregate written by the assign and optimise slices,
/// and that separation is what lets a day be re-planned without touching a single job.
/// </para>
/// <para>
/// The window arrives as two instants rather than as a <c>TimeWindow</c>, following the shape the
/// two slices before this one established: a value object that refuses an impossible value by
/// throwing would do so at the edge, before any validator ran, so an inverted window would surface
/// as an unhandled exception instead of a rejected field.
/// </para>
/// <para>
/// There is no coordinate on it. Where the work happens is the service location's business, and
/// the handler copies the point off it — a caller that could state a location <em>and</em> a
/// different set of coordinates could put a job somewhere the customer's site is not.
/// </para>
/// </remarks>
public sealed record CreateJobCommand(
    CustomerId CustomerId,
    ServiceLocationId LocationId,
    string RequiredSkill,
    JobPriority Priority,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    TimeSpan EstimatedDuration) : ICommand<JobId>;
