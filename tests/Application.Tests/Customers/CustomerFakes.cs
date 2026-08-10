using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Tests.Customers;

/// <summary>
/// <see cref="ICustomerRepository"/> over a <see cref="FakeStore{TAggregate}"/>, scoped to a
/// tenant like the real one.
/// </summary>
/// <remarks>
/// It filters by organization because the real repository does not have to: EF applies a global
/// query filter, so no query in Infrastructure mentions a tenant. A fake that ignored scope would
/// make a handler that stamped the wrong organization onto a new customer look like it worked.
/// </remarks>
internal sealed class FakeCustomerRepository(FakeStore<Customer> store, ITenantContext tenant)
    : ICustomerRepository
{
    public Task<Customer?> GetAsync(CustomerId id, CancellationToken ct) =>
        Task.FromResult(store.Owned(tenant.OrgId).FirstOrDefault(customer => customer.Id == id));

    public void Add(Customer customer) => store.Stage(customer);

    public Task<IReadOnlyList<Customer>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Customer>>(
        [
            .. store.Owned(tenant.OrgId)
                .OrderBy(customer => customer.Name, StringComparer.Ordinal)
                .ThenBy(customer => customer.Id.Value),
        ]);
}
