using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Tests.Customers;
using OpenDispatch.Application.Tests.Technicians;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Technicians;

namespace OpenDispatch.Application.Tests.Fakes;

/// <summary>
/// One feature slice as a request reaches it: the real pipeline, the real handlers and the real
/// validators, over fakes for the ports that would otherwise be a database.
/// </summary>
/// <remarks>
/// <para>
/// Nothing about a slice is registered here. <c>AddApplication</c> finds the handlers and the
/// validators by scanning the application assembly, exactly as the host does — so a handler that
/// is not discoverable, or a validator that is not registered, fails these tests rather than being
/// noticed at the first request in step 47. Only the ports are supplied.
/// </para>
/// <para>
/// Every send gets its own scope, because every request does. A repository and a unit of work
/// shared across two requests would let a test pass on state that a real second request would have
/// to read back out of the database.
/// </para>
/// </remarks>
internal sealed class SliceHost : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private SliceHost(Action<IServiceCollection> ports)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<ITenantContext>(new FixedTenant(Tenant))
            .AddScoped<IUnitOfWork, FakeUnitOfWork>();

        ports(services);

        _services = services.AddApplication().BuildServiceProvider(validateScopes: true);
    }

    /// <summary>The organization every request in this test acts as.</summary>
    public OrgId Tenant { get; } = OrgId.New();

    /// <summary>The Customers slice over a fake customer repository.</summary>
    public static SliceHost Customers() => new(services => services
        .AddStore<Customer>(customer => customer.OrgId)
        .AddScoped<ICustomerRepository, FakeCustomerRepository>());

    /// <summary>The Technicians slice over a fake technician repository.</summary>
    public static SliceHost Technicians() => new(services => services
        .AddStore<Technician>(technician => technician.OrgId)
        .AddScoped<ITechnicianRepository, FakeTechnicianRepository>());

    /// <summary>What the fake database holds for one aggregate.</summary>
    /// <typeparam name="TAggregate">The aggregate root being stored.</typeparam>
    public FakeStore<TAggregate> Store<TAggregate>() =>
        _services.GetRequiredService<FakeStore<TAggregate>>();

    /// <summary>Sends one request through the whole pipeline, in a scope of its own.</summary>
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request)
    {
        using var scope = _services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
