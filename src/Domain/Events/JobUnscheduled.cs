using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Domain.Events;

/// <summary>
/// A job has lost its place in the day and is waiting to be planned again.
/// </summary>
/// <remarks>
/// Raised when the optimiser cannot fit work it had previously placed. It is the one event in the
/// catalog that describes a job moving <em>backwards</em>, and it exists because the alternative —
/// leaving the job labelled <c>Scheduled</c> with nothing planned for it — is the board telling a
/// dispatcher something false.
/// </remarks>
public sealed record JobUnscheduled(JobId JobId) : IDomainEvent
{
    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
