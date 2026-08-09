using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Loads and stores invoices, and with them the lines they own.
/// </summary>
/// <remarks>
/// Two methods, because nothing yet asks a second question. In particular there is no
/// "invoice for this job" lookup: a job may only be billed once, and the job's own state
/// machine already enforces that — the transition to <c>Invoiced</c> is available exactly
/// once, so a duplicate invoice is refused by the domain rather than by a query the handler
/// has to remember to run first.
/// </remarks>
public interface IInvoiceRepository
{
    /// <summary>
    /// Fetches one invoice, with its lines, for changing.
    /// </summary>
    /// <returns>The invoice, or <see langword="null"/> if this tenant has no such invoice.</returns>
    Task<Invoice?> GetAsync(InvoiceId id, CancellationToken ct);

    /// <summary>Stages a newly raised invoice.</summary>
    void Add(Invoice invoice);
}
