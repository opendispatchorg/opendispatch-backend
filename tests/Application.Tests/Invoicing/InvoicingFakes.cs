using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Tests.Invoicing;

/// <summary>
/// <see cref="IInvoiceRepository"/> over a <see cref="FakeStore{TAggregate}"/>, scoped to a tenant
/// like the real one.
/// </summary>
internal sealed class FakeInvoiceRepository(FakeStore<Invoice> store, ITenantContext tenant)
    : IInvoiceRepository
{
    public Task<Invoice?> GetAsync(InvoiceId id, CancellationToken ct) =>
        Task.FromResult(store.Owned(tenant.OrgId).FirstOrDefault(invoice => invoice.Id == id));

    public void Add(Invoice invoice) => store.Stage(invoice);

    public async IAsyncEnumerable<Invoice> StreamAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var invoice in store.Owned(tenant.OrgId)
            .OrderBy(invoice => invoice.Issued)
            .ThenBy(invoice => invoice.Id.Value))
        {
            yield return invoice;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>
/// A payment gateway a test can point either way.
/// </summary>
/// <remarks>
/// The real v1 adapter always succeeds, which is the product decision rather than a limitation —
/// so a refusal is a case only a fake can produce, and it has to be producible: the handler's
/// ordering, money first and marking second, only means anything if something can decline.
/// </remarks>
internal sealed class ControllableGateway : IPaymentGateway
{
    /// <summary>Why the next charge will be refused, or <see langword="null"/> to take the money.</summary>
    public string? Refusing { get; set; }

    /// <summary>What was charged, in the order it was charged.</summary>
    public List<(Money Amount, InvoiceId Invoice)> Charges { get; } = [];

    public Task<PaymentResult> ChargeAsync(Money amount, InvoiceId invoice, CancellationToken ct)
    {
        Charges.Add((amount, invoice));

        return Task.FromResult(Refusing is null
            ? PaymentResult.Taken($"test-{invoice.Value:N}")
            : PaymentResult.Refused(Refusing));
    }
}
