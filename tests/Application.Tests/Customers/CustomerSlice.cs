using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Tests.Customers;

/// <summary>
/// The Customers slice as a request reaches it: the real pipeline, the real handlers and the real
/// validators, over fakes for the two ports that would otherwise be a database.
/// </summary>
/// <remarks>
/// <para>
/// Nothing about the slice is registered here. <c>AddApplication</c> finds the handlers and the
/// validators by scanning the application assembly, exactly as the host does — so a handler that
/// is not discoverable, or a validator that is not registered, fails these tests rather than
/// being noticed at the first request in step 47.
/// </para>
/// <para>
/// Every send gets its own scope, because every request does. A repository and a unit of work
/// shared across two requests would let a test pass on state that a real second request would
/// have to read back out of the database.
/// </para>
/// </remarks>
internal sealed class CustomerSlice : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    public CustomerSlice()
    {
        _services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<CustomerStore>()
            .AddSingleton<ITenantContext>(new FixedTenant(Tenant))
            .AddScoped<ICustomerRepository, FakeCustomerRepository>()
            .AddScoped<IUnitOfWork, FakeUnitOfWork>()
            .AddApplication()
            .BuildServiceProvider(validateScopes: true);
    }

    /// <summary>The organization every request in this test acts as.</summary>
    public OrgId Tenant { get; } = OrgId.New();

    /// <summary>What the fake database holds.</summary>
    public CustomerStore Store => _services.GetRequiredService<CustomerStore>();

    /// <summary>Sends one request through the whole pipeline, in a scope of its own.</summary>
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request)
    {
        using var scope = _services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
