using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Tests.Customers;

/// <summary>
/// The customers a fake database holds, and the ones a fake transaction has not written yet.
/// </summary>
/// <remarks>
/// <para>
/// The split is what makes it worth having rather than a dictionary. The real ports stage and
/// save separately — a repository never writes, and a command's work becomes real when the
/// pipeline commits — so a store that made an added customer immediately readable would let a
/// pipeline that simply forgot to save pass every test here.
/// </para>
/// <para>
/// What it cannot model is a change to a customer that is already saved: the aggregate is one
/// object and the handler mutates it, so a rolled-back <c>AddServiceLocation</c> would still show
/// its location here. That claim is about a database, and is proved against Postgres in
/// <c>Api.IntegrationTests</c>.
/// </para>
/// </remarks>
internal sealed class CustomerStore
{
    private readonly List<Customer> _saved = [];
    private readonly List<Customer> _pending = [];

    /// <summary>Everything written, whichever tenant it belongs to.</summary>
    public IReadOnlyList<Customer> Saved => _saved;

    /// <summary>Stages a customer, unwritten until something saves.</summary>
    public void Stage(Customer customer) => _pending.Add(customer);

    /// <summary>Writes what has been staged.</summary>
    public void Write()
    {
        _saved.AddRange(_pending);
        _pending.Clear();
    }

    /// <summary>Throws away what has been staged and not written.</summary>
    public void Discard() => _pending.Clear();

    /// <summary>What one tenant can see — the fake's version of the global query filter.</summary>
    public IEnumerable<Customer> Owned(OrgId tenant) =>
        _saved.Where(customer => customer.OrgId == tenant);
}

/// <summary>
/// <see cref="ICustomerRepository"/> over the store, scoped to a tenant like the real one.
/// </summary>
/// <remarks>
/// It filters by organization because the real repository does not have to: EF applies a global
/// query filter, so no query in Infrastructure mentions a tenant. A fake that ignored scope would
/// make a handler that stamped the wrong organization onto a new customer look like it worked.
/// </remarks>
internal sealed class FakeCustomerRepository(CustomerStore store, ITenantContext tenant)
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

/// <summary>A unit of work that writes to the store instead of a database.</summary>
internal sealed class FakeUnitOfWork(CustomerStore store) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        store.Write();
        return Task.FromResult(0);
    }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct) =>
        Task.FromResult<IUnitOfWorkTransaction>(new Transaction(store));

    private sealed class Transaction(CustomerStore store) : IUnitOfWorkTransaction
    {
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken ct)
        {
            store.Discard();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>A tenant context that acts as one organization for the life of a test.</summary>
internal sealed class FixedTenant(OrgId orgId) : ITenantContext
{
    public OrgId OrgId { get; } = orgId;
}
