using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Api.IntegrationTests.Fixtures;

/// <summary>
/// Boots the real Api host in-process. Endpoint tests go through this rather than
/// <see cref="WebApplicationFactory{TEntryPoint}"/> directly, so the whole suite picks up
/// host-level test configuration from one place.
/// </summary>
/// <remarks>
/// The user-seeding helper below belongs here per this class's own earlier remark: it is added
/// alongside the feature it exercises, step 44, since there was no auth to seed for before it.
/// Issuing a token and setting the tenant are still to come — step 44 has no
/// <c>ITenantContext</c> caller, and step 45 is what will need the second.
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// Points the host at a database, typically the shared <see cref="PostgresFixture"/>
    /// container. Leave null for endpoints that never touch one.
    /// </summary>
    /// <remarks>
    /// Set this before the first <c>CreateClient()</c> call: that is what builds the host,
    /// and configuration is read once during the build. A settable property rather than a
    /// constructor parameter because xunit requires a class fixture to expose exactly one
    /// public constructor.
    /// </remarks>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Seeds one user directly into the host's <see cref="IUserStore"/> — the store is an
    /// in-memory singleton (Document 3, step 44), so this needs no database and survives for the
    /// life of the factory, exactly like a real deployment's own seed.
    /// </summary>
    /// <param name="orgId">The tenant this user signs in as.</param>
    /// <param name="username">Looked up case-insensitively; must be unique across the store.</param>
    /// <param name="password">The plaintext password a test will post to <c>/auth/login</c>.</param>
    /// <param name="role">What they are allowed to do.</param>
    public async Task<AuthUser> SeedUserAsync(
        OrgId orgId,
        string username,
        string password,
        UserRole role)
    {
        var hasher = Services.GetRequiredService<IPasswordHasher>();
        var users = Services.GetRequiredService<IUserStore>();

        var user = new AuthUser(UserId.New(), orgId, username, hasher.Hash(password), role);
        await users.AddAsync(user, CancellationToken.None).ConfigureAwait(false);

        return user;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        if (ConnectionString is null)
        {
            return;
        }

        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = ConnectionString,
            }));
    }
}
