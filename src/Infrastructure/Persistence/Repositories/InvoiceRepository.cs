using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;

namespace OpenDispatch.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="IInvoiceRepository"/>
/// <remarks>
/// The lines come back with the invoice for the same reason a customer's locations do: they are
/// owned, so they are part of the root rather than a join the caller has to ask for.
/// </remarks>
internal sealed class InvoiceRepository(AppDbContext context) : IInvoiceRepository
{
    public Task<Invoice?> GetAsync(InvoiceId id, CancellationToken ct) =>
        context.Invoices.FirstOrDefaultAsync(invoice => invoice.Id == id, ct);

    public void Add(Invoice invoice) => context.Invoices.Add(invoice);
}
