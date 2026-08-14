using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Infrastructure.Attachments;
using OpenDispatch.Infrastructure.Auth;
using OpenDispatch.Infrastructure.Payments;
using OpenDispatch.Infrastructure.Persistence;
using OpenDispatch.Infrastructure.Provisioning;
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

            // The user store, password hasher and JWT issuer (Document 2 §7, step 44). The store
            // reads the users table through the context AddPersistence registers, so it goes after
            // it — beside it rather than inside it, because who may sign in is not a business
            // record and does not belong in the same registration as the aggregates.
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
            .AddSingleton<IPaymentGateway, FakePaymentGateway>()

            // How a deployment gets its first login (`create-user`). Registered in every
            // environment, unlike the demo seeder beside it: a production host is precisely the one
            // that has to do this once, and doing it is the only way anybody can sign in.
            // Scoped, because it writes through the request-shaped context the verb opens a scope
            // for.
            .AddScoped<UserProvisioner>()

            // Housekeeping for the two tables nothing else deletes from (`prune`). Registered
            // everywhere for the same reason the provisioner is: it is a production host that
            // eventually has a year of op log to be rid of.
            .AddScoped<SyncLogPruner>();
}
