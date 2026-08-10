using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
    /// plugin enabled.
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
        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseNpgsql(
                connectionString(provider),
                // Turns on the NTS type handlers, so a GeoPoint can be stored as
                // geography(Point) and queried with PostGIS operators from step 25 onwards.
                npgsql => npgsql.UseNetTopologySuite()));

        return services;
    }
}
