using System.Globalization;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Infrastructure.Payments;

/// <summary>
/// The v1 payment gateway: it always succeeds, because v1 does not take payment.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Not a stub and not temporary.</strong> Document 1 puts real payment processing
/// explicitly out of scope and says invoicing stops at "mark paid" — so this is the whole of what
/// the first version offers, and marking a bill settled is a bookkeeping act rather than a
/// financial one. Naming it a fake describes the money, not the code's maturity.
/// </para>
/// <para>
/// It exists as an adapter rather than as an <c>if</c> in the handler so that the roadmap's first
/// extension is a registration change. A real processor is one class beside this one, and nothing
/// in Application or Domain has heard of either.
/// </para>
/// <para>
/// The reference it invents is marked as what it is. Nothing stores it today, but a
/// <c>fake-</c> prefix in a log or a future column is unambiguous where a plausible-looking
/// transaction id would be a small lie waiting to be reconciled against a statement.
/// </para>
/// </remarks>
internal sealed class FakePaymentGateway : IPaymentGateway
{
    public Task<PaymentResult> ChargeAsync(Money amount, InvoiceId invoice, CancellationToken ct) =>
        Task.FromResult(PaymentResult.Taken(
            string.Create(CultureInfo.InvariantCulture, $"fake-{invoice.Value:N}")));
}
