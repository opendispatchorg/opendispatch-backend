using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;
using OpenDispatch.Application.Sync;
using OpenDispatch.Application.Tests.Customers;
using OpenDispatch.Application.Tests.Invoicing;
using OpenDispatch.Application.Tests.Jobs;
using OpenDispatch.Application.Tests.Sync;
using OpenDispatch.Application.Tests.Technicians;
using OpenDispatch.Domain.Assignments;
using OpenDispatch.Domain.Customers;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.Technicians;
using OpenDispatch.Scheduling;
using OpenDispatch.Scheduling.Travel;

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
            .AddSingleton(Clock)
            .AddSingleton<IClock>(Clock)

            // The pipeline audits every command, so every slice needs somewhere for that to go and
            // somebody to attribute it to — the same two things a real host resolves per request.
            .AddSingleton(Audit)
            .AddSingleton<IAuditLog>(Audit)
            .AddSingleton<ICallerContext>(new FixedCaller(Caller, CallerName))
            .AddScoped<IUnitOfWork, FakeUnitOfWork>();

        ports(services);

        _services = services.AddApplication().BuildServiceProvider(validateScopes: true);
    }

    /// <summary>The organization every request in this test acts as.</summary>
    public OrgId Tenant { get; } = OrgId.New();

    /// <summary>What the handlers think the time is. A test may move it.</summary>
    public FixedClock Clock { get; } = new();

    /// <summary>Who every request in this test is made by.</summary>
    public UserId Caller { get; } = UserId.New();

    /// <summary>What that caller signed in as.</summary>
    public const string CallerName = "dana@vance.example";

    /// <summary>What the pipeline wrote down about what was done.</summary>
    public FakeAuditLog Audit { get; } = new();

    /// <summary>The Customers slice over a fake customer repository.</summary>
    public static SliceHost Customers() => new(services => services
        .AddStore<Customer>(customer => customer.OrgId)
        .AddScoped<ICustomerRepository, FakeCustomerRepository>());

    /// <summary>The Technicians slice over a fake technician repository.</summary>
    public static SliceHost Technicians() => new(services => services
        .AddStore<Technician>(technician => technician.OrgId)
        .AddScoped<ITechnicianRepository, FakeTechnicianRepository>());

    /// <summary>
    /// The Jobs slice, which needs customers too: booking a job resolves the service location it
    /// happens at, so the Customers slice is how a test arranges one.
    /// </summary>
    public static SliceHost Jobs() => new(services => services
        .AddStore<Job>(job => job.OrgId)
        .AddScoped<IJobRepository, FakeJobRepository>()
        .AddStore<Customer>(customer => customer.OrgId)
        .AddScoped<ICustomerRepository, FakeCustomerRepository>());

    /// <summary>
    /// The dispatch paths — manual and optimised: jobs and their customers, the crew, the plan, a
    /// real travel-time provider and the real engine.
    /// </summary>
    /// <remarks>
    /// The travel provider is the engine's own haversine rather than a stub returning a constant.
    /// What the assign path has to get right is <em>which two points</em> it measures between —
    /// the home base for a first stop, the stop before it otherwise — and a provider that answers
    /// the same number for every pair would agree with any of them.
    /// </remarks>
    public static SliceHost Dispatching() => new(services => services
        .AddStore<Job>(job => job.OrgId)
        .AddScoped<IJobRepository, FakeJobRepository>()
        .AddStore<Customer>(customer => customer.OrgId)
        .AddScoped<ICustomerRepository, FakeCustomerRepository>()
        .AddStore<Technician>(technician => technician.OrgId)
        .AddScoped<ITechnicianRepository, FakeTechnicianRepository>()
        .AddStore<Assignment>(assignment => assignment.OrgId)
        .AddScoped<IAssignmentRepository, FakeAssignmentRepository>()
        .AddSingleton<ITravelTimeProvider, HaversineTravelTimeProvider>()

        // The real engine, not a stub. What step 37 has to get right is the translation either
        // side of it — a problem built from rows, a plan written back as stops — and a scheduler
        // that returned a canned answer would agree with a translation that made no sense.
        .AddSingleton<IScheduler, AnnealingScheduler>());

    /// <summary>
    /// The invoicing slice: jobs and their customers, the bills raised against them, and a gateway
    /// a test can make refuse.
    /// </summary>
    public static SliceHost Invoicing() => new(services => services
        .AddStore<Job>(job => job.OrgId)
        .AddScoped<IJobRepository, FakeJobRepository>()
        .AddStore<Customer>(customer => customer.OrgId)
        .AddScoped<ICustomerRepository, FakeCustomerRepository>()
        .AddStore<Invoice>(invoice => invoice.OrgId)
        .AddScoped<IInvoiceRepository, FakeInvoiceRepository>()
        .AddSingleton<ControllableGateway>()
        .AddSingleton<IPaymentGateway>(provider => provider.GetRequiredService<ControllableGateway>()));

    /// <summary>
    /// The sync slice: jobs and their customers, the op log a push dedupes against, and a cursor
    /// source that moves.
    /// </summary>
    /// <remarks>
    /// Customers are here for the same reason as in <see cref="Jobs"/> — a job is arranged through
    /// the slices that own it — and not because sync touches a customer.
    /// </remarks>
    public static SliceHost Sync() => new(services => services
        .AddStore<Job>(job => job.OrgId)
        .AddScoped<IJobRepository, FakeJobRepository>()
        .AddStore<Customer>(customer => customer.OrgId)
        .AddScoped<ICustomerRepository, FakeCustomerRepository>()
        .AddStore<SyncOpRecord>(op => op.OrgId)
        .AddScoped<ISyncOpStore, FakeSyncOpStore>()
        .AddSingleton<CallOrder>()
        .AddSingleton<SteppingCursors>()
        .AddSingleton<ISyncCursorSource>(provider => provider.GetRequiredService<SteppingCursors>())
        .AddSingleton<RecordingChangeReader>()
        .AddSingleton<ISyncChangeReader>(provider => provider.GetRequiredService<RecordingChangeReader>()));

    /// <summary>What the fake database holds for one aggregate.</summary>
    /// <typeparam name="TAggregate">The aggregate root being stored.</typeparam>
    public FakeStore<TAggregate> Store<TAggregate>() =>
        _services.GetRequiredService<FakeStore<TAggregate>>();

    /// <summary>A fake a test needs to steer or inspect — the gateway, so far.</summary>
    /// <typeparam name="TPort">The fake's own type, not the port it implements.</typeparam>
    public TPort Fake<TPort>()
        where TPort : notnull => _services.GetRequiredService<TPort>();

    /// <summary>Sends one request through the whole pipeline, in a scope of its own.</summary>
    public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request)
    {
        using var scope = _services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();
}
