using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Infrastructure.Persistence.Repositories;

namespace OpenDispatch.Infrastructure.Persistence;

/// <summary>
/// Registration for the persistence layer.
/// </summary>
/// <remarks>
/// The composition root is the Api (Document 2 §2), but which provider and which plugins the
/// context needs is Infrastructure's business — so the host supplies a connection string and
/// nothing more. Api never has to name Npgsql or NetTopologySuite.
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
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionString)
    {
        services.AddDbContext<AppDbContext>((provider, options) => options
            .UseNpgsql(
                connectionString(provider),
                // Turns on the NTS type handlers, so a GeoPoint can be stored as
                // geography(Point) and queried with PostGIS operators.
                npgsql => npgsql.UseNetTopologySuite())
            // Tables and columns are snake_case. This runs over whatever names the model ends
            // up with, so a configuration names a table once, in the words the database uses.
            .UseSnakeCaseNamingConvention());

        // Scoped, the same lifetime as the context they share. That sharing is the point: a
        // handler that loads a job through one repository and adds an assignment through another
        // is staging both on one context, which is what lets the unit of work commit them
        // together. A singleton repository here would silently break that.
        services.AddScoped<IJobRepository, JobRepository>();
        services.AddScoped<IAssignmentRepository, AssignmentRepository>();
        services.AddScoped<ITechnicianRepository, TechnicianRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
