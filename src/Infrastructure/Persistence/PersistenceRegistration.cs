using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Infrastructure.Events;
using OpenDispatch.Infrastructure.Persistence.ReadModels;
using OpenDispatch.Infrastructure.Persistence.Repositories;
using OpenDispatch.Infrastructure.Tenancy;

namespace OpenDispatch.Infrastructure.Persistence;

/// <summary>
/// Registration for the persistence layer.
/// </summary>
/// <remarks>
/// <para>
/// The composition root is the Api (Document 2 §2), but which provider and which plugins the
/// context needs is Infrastructure's business — so the host supplies a connection string and
/// nothing more. Api never has to name Npgsql or NetTopologySuite.
/// </para>
/// <para>
/// Since step 31 this needs a publisher to hand domain events to, which
/// <c>AddApplication</c> supplies: saving is what announces what an aggregate did, so a
/// persistence layer with nowhere to announce it is not a working one. Resolving a context
/// without it fails at the first resolve rather than at the first event.
/// </para>
/// </remarks>
public static class PersistenceRegistration
{
    /// <summary>
    /// Registers <see cref="AppDbContext"/> against PostgreSQL with the PostGIS/NetTopologySuite
    /// plugin enabled, and the persistence ports over it.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="connectionString">
    /// Reads the connection string once the container is built. A factory rather than a value
    /// because the host validates its configuration on start, and that validated value does not
    /// exist yet at the point registration runs.
    /// </param>
    /// <param name="outbox">
    /// How the outbox sweep behaves, or <see langword="null"/> for the defaults a deployment wants.
    /// A test suite is the only caller with a reason to change them — see <c>OutboxOptions</c>.
    /// </param>
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionString,
        OutboxOptions? outbox = null)
    {
        // Scoped, all three: one queue, one dispatcher and one interceptor per request, sharing
        // the lifetime of the context that fills the queue and the transaction that empties it.
        services.AddScoped<DomainEventQueue>();
        services.AddScoped<DomainEventDispatcher>();
        services.AddScoped<DomainEventInterceptor>();

        // The sweep that delivers what a request did not — a subscriber that threw, a host killed
        // between committing work and announcing it. A hosted service rather than a verb, unlike
        // `prune`, because it is not a scheduled chore: it is the second half of every write, and
        // it claims its rows in a way two instances can share (see OutboxDispatcher).
        services.AddSingleton(outbox ?? new OutboxOptions());
        services.AddScoped<OutboxSweep>();
        services.AddHostedService<OutboxDispatcher>();

        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseNpgsql(
                connectionString(provider),
                npgsql => npgsql
                    // Turns on the NTS type handlers, so a GeoPoint can be stored as
                    // geography(Point) and queried with PostGIS operators.
                    .UseNetTopologySuite()

                    // A failover, a reset connection, a database that was restarting while a
                    // technician's phone pushed — every one of those reached the caller as a 500
                    // until this line, where a second attempt would have been invisible. The
                    // strategy owns the retry *boundary*, which is why commands go through
                    // IUnitOfWork.ExecuteInTransactionAsync: it refuses to retry a transaction
                    // somebody else opened, and it is right to.
                    //
                    // Three attempts over five seconds: enough to ride out a failover, short
                    // enough that a request does not sit behind a database that is genuinely gone
                    // — that case is readiness' to report, not this one's to wait for.
                    .EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorCodesToAdd: null))
            // Tables and columns are snake_case. This runs over whatever names the model ends
            // up with, so a configuration names a table once, in the words the database uses.
            .UseSnakeCaseNamingConvention()
            // Resolved from the request's own scope, so the events it collects go into that
            // request's queue and no other's.
            .AddInterceptors(provider.GetRequiredService<DomainEventInterceptor>()));

        // The context now depends on knowing whose request it is serving, so the tenant is
        // registered here rather than left to the host: a DbContext registered without one would
        // build a model whose filters cannot be evaluated. Step 45's middleware resolves it;
        // until then reading it throws, which is the point.
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());

        // Scoped, the same lifetime as the context they share. That sharing is the point: a
        // handler that loads a job through one repository and adds an assignment through another
        // is staging both on one context, which is what lets the unit of work commit them
        // together. A singleton repository here would silently break that.
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IAssignmentRepository, AssignmentRepository>();
        services.AddScoped<ITechnicianRepository, TechnicianRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // A read port rather than a repository, and scoped like them because it shares the
        // request's context: the board is read inside the same tenant scope everything else is.
        services.AddScoped<IDispatchBoardReadModel, DispatchBoardReadModel>();

        // The op log stages like a repository and is committed by the same unit of work, so that
        // the record of an operation and its effect land together. The cursor source shares the
        // context for a different reason: a watermark is only true of the connection that asked.
        services.AddScoped<ISyncOpStore, SyncOpStore>();
        services.AddScoped<ISyncCursorSource, SyncCursorSource>();

        // A read port like the board's, and scoped for the same reason: the watermark and the
        // changes read against it have to come from one connection to describe one moment.
        services.AddScoped<ISyncChangeReader, SyncChangeReader>();

        // Readiness (Document 3, step 54). Registered here rather than by the host for the same
        // reason the context is: whether this deployment can serve traffic is a question about the
        // database, and a composition root that has to remember to ask it is one that will
        // eventually forget. The timeout is the probe's own guarantee — a health endpoint that
        // hangs is worse than one that answers "no", because an orchestrator reads no answer at
        // all as a host that has stopped responding and restarts it.
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>(DatabaseHealthCheck.Name, timeout: TimeSpan.FromSeconds(5));

        return services;
    }
}
