using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Application.Abstractions;

namespace OpenDispatch.Infrastructure.Auth;

/// <summary>Registration for authentication: the user store, the hasher, and the token issuer.</summary>
public static class AuthRegistration
{
    /// <summary>
    /// Registers the minimal user store, the password hasher, and the JWT issuer.
    /// </summary>
    /// <param name="services">The host's service collection.</param>
    /// <param name="signingOptions">
    /// Reads the signing key, issuer, audience and expiry once the container is built — a
    /// factory for the same reason the connection string and the attachment root are: the host
    /// validates its configuration on start, and that validated value does not exist yet at the
    /// point registration runs.
    /// </param>
    /// <remarks>
    /// The store is <strong>scoped</strong>, unlike the hasher and the issuer beside it: it reads
    /// and writes through the request's own <c>AppDbContext</c>, which is where users have lived
    /// since they stopped being a process-lifetime dictionary. A caller resolving it from the root
    /// provider — the test factory's seeding helper, a hosted service — has to open a scope, which
    /// is the ordinary arrangement for everything else that touches this database.
    /// </remarks>
    public static IServiceCollection AddAuth(
        this IServiceCollection services,
        Func<IServiceProvider, JwtSigningOptions> signingOptions) =>
        services
            .AddScoped<IUserStore, EfUserStore>()
            .AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>()
            .AddSingleton<ITokenIssuer>(provider => new JwtTokenIssuer(signingOptions(provider)));
}
