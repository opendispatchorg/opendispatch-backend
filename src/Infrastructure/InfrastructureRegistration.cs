using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Infrastructure.Persistence;
using OpenDispatch.Infrastructure.Time;
using OpenDispatch.Scheduling.Travel;

namespace OpenDispatch.Infrastructure;

/// <summary>
/// Everything the outside world supplies, in one call.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the count went from one to three in two steps: persistence, then a clock,
/// then a travel-time provider. Three registrations called from two places — the composition root
/// and the integration harness that stands in for it — is three chances for the harness to compose
/// half a host and for a test to pass against a system nobody runs.
/// </para>
/// <para>
/// The parts stay individually available. A test that genuinely wants persistence and nothing else
/// can still say so; what it can no longer do by accident is <em>miss</em> something the host has.
/// </para>
/// </remarks>
public static class InfrastructureRegistration
{
    /// <summary>
    /// Registers the persistence layer, the clock, and the travel-time provider.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="connectionString">Reads the connection string once the container is built.</param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionString) =>
        services
            .AddPersistence(connectionString)
            .AddSystemClock()

            // Straight-line travel: the default that ships with the engine and needs no
            // infrastructure at all. The road-network provider Document 2 §4 describes is a class
            // in this project and one changed line here — the solver, and now the manual assign
            // path, do not know the difference.
            //
            // A singleton because implementations must be safe to call from several threads and
            // must give the same answer for the same pair every time; the haversine holds nothing.
            .AddSingleton<ITravelTimeProvider, HaversineTravelTimeProvider>();
}
