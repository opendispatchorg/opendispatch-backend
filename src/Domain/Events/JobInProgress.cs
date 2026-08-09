using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Domain.Events;

/// <summary>
/// A technician has started work on a job.
/// </summary>
public sealed record JobInProgress(JobId JobId) : IDomainEvent
{
    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
