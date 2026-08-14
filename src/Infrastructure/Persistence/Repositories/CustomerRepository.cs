using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Infrastructure.Persistence.Repositories;

/// <inheritdoc cref="ICustomerRepository"/>
/// <remarks>
/// Neither query mentions the service locations, and both return them: an owned collection is
/// part of its root, so EF loads it with the root and there is no <c>Include</c> to forget. That
/// is the aggregate boundary being enforced by the mapping rather than by every call site.
/// </remarks>
internal sealed class CustomerRepository(AppDbContext context) : ICustomerRepository
{
    public Task<Customer?> GetAsync(CustomerId id, CancellationToken ct) =>
        context.Customers.FirstOrDefaultAsync(customer => customer.Id == id, ct);

    public void Add(Customer customer) => context.Customers.Add(customer);

    /// <remarks>
    /// <para>
    /// By name, because this one is read by a person choosing from a list — unlike the crew,
    /// which is read by the scheduler. The id breaks ties, which is what stops two customers with
    /// one name swapping places between page one and page two and hiding each other.
    /// </para>
    /// <para>
    /// Two round trips: the count, then the page. The alternative — a window function carrying the
    /// total on every row — reads the same rows and costs a wider result set, and the count is
    /// answered from the tenant index without touching the rows at all.
    /// </para>
    /// </remarks>
    public async Task<Page<Customer>> ListAsync(PageRequest page, CancellationToken ct)
    {
        var total = await context.Customers.CountAsync(ct).ConfigureAwait(false);

        var items = await context.Customers
            .OrderBy(customer => customer.Name)
            .ThenBy(customer => customer.Id)
            .Skip(page.Skip)
            .Take(page.Size)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return new Page<Customer>(items, total);
    }

    /// <remarks>
    /// <c>AsAsyncEnumerable</c> rather than <c>ToListAsync</c>: the export reads every customer a
    /// shop has ever had, and the point of streaming it is that the whole of it is never in memory
    /// at once — not in the repository, not in the handler, and not in a serialized response body.
    /// </remarks>
    public IAsyncEnumerable<Customer> StreamAsync(CancellationToken ct) =>
        context.Customers
            .OrderBy(customer => customer.Name)
            .ThenBy(customer => customer.Id)
            .AsAsyncEnumerable();
}
