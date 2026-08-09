using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Takes money.
/// </summary>
/// <remarks>
/// <para>
/// Declared now although v1 does not process payments at all, because it is the seam the
/// roadmap's first extension arrives through: a real processor is one adapter class in
/// Infrastructure and a registration change, with nothing in Application or Domain touched.
/// The port justifies itself by being on the roadmap (Document 2 §13) rather than by being
/// imaginable.
/// </para>
/// <para>
/// v1's adapter always succeeds, which is a scoped product decision and not a stub — "mark
/// paid" is what the first version offers. It is still routed through this port so that the
/// day it is replaced, the code that calls it does not change.
/// </para>
/// <para>
/// There is no payment method, card token or customer here. v1 has nothing to put in one,
/// and a parameter invented for a processor nobody has chosen would be wrong in a specific
/// way rather than absent in an obvious one.
/// </para>
/// </remarks>
public interface IPaymentGateway
{
    /// <summary>
    /// Charges an amount against an invoice.
    /// </summary>
    /// <remarks>
    /// The invoice's identity travels with the charge because a payment has to be traceable
    /// to what it settled, and because it is the natural idempotency key: a processor handed
    /// the same reference twice must not take the money twice, and a "mark paid" retried
    /// after a timeout is an ordinary Tuesday.
    /// </remarks>
    /// <returns>Whether the money was taken, and what to record about it.</returns>
    Task<PaymentResult> ChargeAsync(Money amount, InvoiceId invoice, CancellationToken ct);
}
