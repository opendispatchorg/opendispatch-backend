using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Infrastructure.Attachments;
using OpenDispatch.Infrastructure.Auth;
using OpenDispatch.Infrastructure.Payments;
using OpenDispatch.Infrastructure.Persistence;
using OpenDispatch.Infrastructure.Time;
using OpenDispatch.Scheduling;
using OpenDispatch.Scheduling.Travel;

namespace OpenDispatch.Infrastructure;

/// <summary>
/// Everything the outside world supplies, in one call.
/// </summary>
/// <remarks>
/// <para>
/// It exists because the count went from one to three in two steps: persistence, then a clock,
/// then a travel-time provider — and attachment storage since. Registrations called from two places — the composition root
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
    /// <param name="attachmentRoot">
    /// Reads the directory attachment content is stored under, once the container is built.
    /// </param>
    /// <param name="jwtSigningOptions">
    /// Reads the JWT signing key, issuer, audience and expiry once the container is built.
    /// </param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionString,
        Func<IServiceProvider, string> attachmentRoot,
        Func<IServiceProvider, JwtSigningOptions> jwtSigningOptions) =>
        services
            .AddPersistence(connectionString)
            .AddSystemClock()

            // The minimal user store, password hasher and JWT issuer (Document 2 §7, step 44).
            // Auth has no database of its own yet — see InMemoryUserStore's remarks — so it asks
            // for nothing from persistence and sits beside it rather than inside AddPersistence.
            .AddAuth(jwtSigningOptions)

            // Photographs and signatures on a local disk, which is the whole answer for a shop
            // hosting this itself (Document 1). A bucket adapter is one class beside it and this
            // line changed; nothing above the port knows the difference.
            .AddLocalAttachmentStorage(attachmentRoot)

            // Straight-line travel: the default that ships with the engine and needs no
            // infrastructure at all. The road-network provider Document 2 §4 describes is a class
            // in this project and one changed line here — the solver, and now the manual assign
            // path, do not know the difference.
            //
            // A singleton because implementations must be safe to call from several threads and
            // must give the same answer for the same pair every time; the haversine holds nothing.
            .AddSingleton<ITravelTimeProvider, HaversineTravelTimeProvider>()

            // The engine. A singleton for the same reason: it holds the travel provider and its
            // search settings and nothing else — everything that varies between runs arrives in
            // the problem, including the seed that makes a plan reproducible.
            .AddSingleton<IScheduler, AnnealingScheduler>()

            // v1 takes no money, which is a scoped product decision (Document 1) rather than an
            // unfinished adapter. A real processor is one class beside this one and one changed
            // line here.
            .AddSingleton<IPaymentGateway, FakePaymentGateway>();
}
