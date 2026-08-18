using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Domain.Events;

/// <summary>
/// A bill has been raised for a job. The moment the work becomes something owed.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="InvoicePaid"/> because they are opposite ends of the same transaction
/// and only one of them is a request for money. A customer wants both — "here is what you owe" and
/// "thank you, that is settled" — and a shop that only ever sent the second would be missing the
/// step between doing the work and getting paid.
/// </para>
/// <para>
/// Raised by the factories rather than announced by the handler that calls them, for the reason
/// <see cref="AssignmentPlanned"/> gives: two of them build invoices, and a rule every caller has to
/// remember is a rule the next caller forgets.
/// </para>
/// <para>
/// <strong>It carries no total.</strong> An invoice built by <c>CreateFromJob</c> has its lines
/// added after the factory returns, so a total on this event would be zero for half the callers and
/// right for the other half. Subscribers re-read the aggregate — which they must anyway, since the
/// event reaches them after the commit — and read the total there.
/// </para>
/// </remarks>
/// <param name="InvoiceId">The bill that now exists.</param>
/// <param name="JobId">The work it bills for, carried so subscribers need no lookup.</param>
public sealed record InvoiceRaised(InvoiceId InvoiceId, JobId JobId) : IDomainEvent
{
    /// <inheritdoc />
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}
