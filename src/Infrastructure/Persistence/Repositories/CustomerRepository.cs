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
    /// By name, because this one is read by a person choosing from a list — unlike the crew,
    /// which is read by the scheduler.
    /// </remarks>
    public async Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct) =>
        await context.Customers
            .OrderBy(customer => customer.Name)
            .ThenBy(customer => customer.Id)
            .ToListAsync(ct);
}
