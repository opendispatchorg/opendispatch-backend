using Microsoft.Extensions.DependencyInjection;

namespace OpenDispatch.Infrastructure.Seeding;

/// <summary>
/// The guard on the demo seeder, and the registration that enforces it.
/// </summary>
/// <remarks>
/// <para>
/// Document 3, step 53: "guard it so it cannot run against production config". The guard is the
/// registration — outside Development <see cref="DemoSeeder"/> is not in the container at all, so
/// there is nothing for a stray call, a forgotten command-line argument or a future endpoint to
/// resolve. A boolean the seeder checked would be a guard something could pass the wrong value to;
/// a type that does not exist is not.
/// </para>
/// <para>
/// The environment is the signal the guard reads because it is the one the host already treats as
/// authoritative about what kind of deployment this is — the same signal that decides whether
/// <c>/openapi</c> is served. It is not a claim about the connection string: a developer who points
/// a Development host at a production database has already defeated more than this. What that case
/// still has is the seeder's second guard, which is that every row it writes or deletes belongs to
/// an organization named <see cref="DemoData.OrganizationName"/>.
/// </para>
/// </remarks>
public static class DemoSeeding
{
    /// <summary>The only environment the demo seeder may exist in.</summary>
    public const string RequiredEnvironment = "Development";

    /// <summary>Whether the demo seeder may run against a host in this environment.</summary>
    /// <param name="environmentName">The host's environment name.</param>
    public static bool IsAllowedIn(string? environmentName) =>
        string.Equals(environmentName, RequiredEnvironment, StringComparison.Ordinal);

    /// <summary>
    /// Registers <see cref="DemoSeeder"/> if — and only if — this host is a development one.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="environmentName">The host's environment name.</param>
    /// <remarks>
    /// Scoped, because the context it writes through is. Callers create a scope of their own: the
    /// seeder runs outside any request.
    /// </remarks>
    public static IServiceCollection AddDemoSeeding(
        this IServiceCollection services,
        string? environmentName) =>
        IsAllowedIn(environmentName) ? services.AddScoped<DemoSeeder>() : services;
}
