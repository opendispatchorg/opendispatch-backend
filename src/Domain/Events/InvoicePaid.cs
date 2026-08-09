using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Domain.Events;

/// <summary>
/// An invoice has been settled. The end of the call-to-cash loop, and the seam anything
/// that cares about money changing hands hangs off — moving the job to Paid, technician
/// commission, an accounting export.
/// </summary>
/// <param name="InvoiceId">The invoice that was settled.</param>
/// <param name="JobId">The job it was raised for, carried so subscribers need no lookup.</param>
public sealed record InvoicePaid(InvoiceId InvoiceId, JobId JobId) : IDomainEvent
{
    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
