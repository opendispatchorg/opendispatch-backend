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

    /// <remarks>
    /// By issue date: a billing history is read in the order it happened. Streamed for the export,
    /// its only caller.
    /// <para>
    /// <strong>No-tracking, and that is not an optimisation.</strong> A tracked stream puts every row
    /// it hands out into the change tracker and holds it there until the request ends — so an export
    /// that streams precisely so a shop's history need not be held in memory would hold all of it
    /// anyway, one identity map at a time. Measured on a year of history: the peak came down by
    /// roughly a third. Nothing saves a projection, so there is nothing to track for.
    /// </para>
    /// </remarks>
    public IAsyncEnumerable<Invoice> StreamAsync(CancellationToken ct) =>
        context.Invoices
            .AsNoTracking()
            .OrderBy(invoice => invoice.Issued)
            .ThenBy(invoice => invoice.Id)
            .AsAsyncEnumerable();
}
