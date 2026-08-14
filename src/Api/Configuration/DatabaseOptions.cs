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
    /// <param name="services">The host's service collection.</param>
    /// <param name="configuration">Where the section is bound from.</param>
    /// <param name="environment">
    /// Decides whether the committed development password is acceptable — see
    /// <see cref="DevelopmentDefaults"/>.
    /// </param>
    public static IServiceCollection AddDatabaseOptions(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services
            .AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => DevelopmentDefaults.AreAllowedIn(environment)
                    || !options.ConnectionString.Contains(
                        DevelopmentDefaults.DatabasePassword,
                        StringComparison.OrdinalIgnoreCase),
                $"{DatabaseOptions.SectionName}:ConnectionString still carries the development password this "
                + "repository commits, which is published in appsettings.json and "
                + "docker-compose.yml. Set a real one for this environment.")
            .ValidateOnStart();

        return services;
    }
}
