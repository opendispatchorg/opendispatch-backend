using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Application;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Infrastructure;
using OpenDispatch.Infrastructure.Auth;
using OpenDispatch.Infrastructure.Events;

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
    /// <remarks>
    /// Attachment content goes to a directory of this run's own, deleted with the fixture. A test
    /// that wrote photographs into the repository would be one nobody notices until it is committed.
    /// </remarks>
    public static IServiceCollection Over(PostgresFixture postgres, OutboxOptions? outbox = null) =>
        new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddInfrastructure(
                _ => postgres.ConnectionString,
                _ => postgres.AttachmentRoot,
                // None of these tests are about auth, so the signing key only has to satisfy
                // JwtTokenIssuer's constructor — nothing here issues or reads a real token.
                _ => new JwtSigningOptions(
                    "test-host-signing-key-not-for-production-use-ever",
                    "opendispatch-tests",
                    "opendispatch-tests",
                    TimeSpan.FromHours(1)),

                // The sweep is off unless a test asks for it. These containers have no host to run
                // a background service anyway, and the one class that tests the outbox drives the
                // dispatcher directly rather than waiting for a timer.
                outbox ?? new OutboxOptions(Enabled: false))
            // Last, so it replaces the real tenant context: nothing resolves one from a
            // principal until step 45.
            .AddScoped<TestTenantContext>()
            .AddScoped<ITenantContext>(provider => provider.GetRequiredService<TestTenantContext>())
            // The real adapter lives in Api and needs a running host (step 51); this satisfies
            // BoardNotifications' unconditional subscription without one.
            .AddScoped<IBoardNotifier, NoOpBoardNotifier>();

    /// <summary>A scope acting as one organization — one request's worth of services.</summary>
    public static IServiceScope ActingAs(this ServiceProvider services, OrgId tenant)
    {
        var scope = services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TestTenantContext>().ActAs(tenant);

        return scope;
    }
}
