using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Domain.Events;

/// <summary>
/// The work on a job is finished. The event the rest of the roadmap hangs off — invoicing,
/// the customer survey, inventory decrement and technician commission all arrive as
/// handlers for this, never as edits to the job.
/// </summary>
/// <param name="JobId">The job that was completed.</param>
/// <param name="OccurredAt">
/// When the work actually finished, which is not necessarily when the server heard about
/// it. A technician completes a job in a basement with no signal and the phone syncs an
/// hour later; the domain records the former.
/// </param>
public sealed record JobCompleted(JobId JobId, DateTimeOffset OccurredAt) : IDomainEvent;
