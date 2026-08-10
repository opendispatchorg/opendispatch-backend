using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Application;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Infrastructure.Persistence;
using OpenDispatch.Infrastructure.Time;

namespace OpenDispatch.Api.IntegrationTests.Fixtures;

/// <summary>
/// The host's own composition — the application pipeline over the persistence layer — with a
/// tenant a test can state.
/// </summary>
/// <remarks>
/// <para>
/// Everything a request has and nothing a request does not. A test adds its own handlers on top
/// and changes nothing else, so a behavior registered in the wrong order or a service registered
/// with the wrong lifetime fails these tests rather than surviving until production.
/// </para>
/// <para>
/// Separate from <see cref="PostgresFixture"/>, whose provider is built once for the whole run:
/// a test that registers its own handlers needs a container of its own, and the schema is the
/// only thing worth sharing.
/// </para>
/// </remarks>
internal static class TestHost
{
    /// <summary>The real registrations, over the shared container's database.</summary>
    public static IServiceCollection Over(PostgresFixture postgres) =>
        new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddPersistence(_ => postgres.ConnectionString)
            .AddSystemClock()
            // Last, so it replaces the real tenant context: nothing resolves one from a
            // principal until step 45.
            .AddScoped<TestTenantContext>()
            .AddScoped<ITenantContext>(provider => provider.GetRequiredService<TestTenantContext>());

    /// <summary>A scope acting as one organization — one request's worth of services.</summary>
    public static IServiceScope ActingAs(this ServiceProvider services, OrgId tenant)
    {
        var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestTenantContext>().ActAs(tenant);

        return scope;
    }
}
