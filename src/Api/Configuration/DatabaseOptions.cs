using System.ComponentModel.DataAnnotations;

namespace OpenDispatch.Api.Configuration;

/// <summary>
/// Connection settings for the PostgreSQL/PostGIS database, bound from the
/// <c>Database</c> configuration section.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; init; } = string.Empty;
}

/// <summary>
/// Registration for <see cref="DatabaseOptions"/>.
/// </summary>
public static class DatabaseOptionsRegistration
{
    /// <summary>
    /// Binds and validates <see cref="DatabaseOptions"/>. Validation runs at startup so a
    /// host with a missing connection string fails immediately and loudly, rather than at
    /// the first query on a request path.
    /// </summary>
    public static IServiceCollection AddDatabaseOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        return services;
    }
}
