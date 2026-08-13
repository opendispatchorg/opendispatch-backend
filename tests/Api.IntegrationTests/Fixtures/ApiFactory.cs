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
    /// Where a host with no <see cref="ConnectionString"/> is pointed instead of at whatever
    /// <c>appsettings.json</c> names.
    /// </summary>
    /// <remarks>
    /// <c>.invalid</c> never resolves, by RFC 2606, so a test that reaches a database without
    /// having asked for one fails against a host that does not exist rather than against the
    /// developer's own <c>docker compose</c> database — which is exactly what
    /// <c>appsettings.json</c>'s <c>Host=localhost;Port=5433</c> would otherwise hand it. The
    /// name is the error message: it shows up verbatim in the Npgsql failure.
    /// </remarks>
    private const string NoDatabaseRequested =
        "Host=api-factory-connection-string-not-set.invalid;Database=none;Username=none;Password=none";

    /// <summary>
    /// Points the host at a database, typically the shared <see cref="PostgresFixture"/>
    /// container. Leave null only for endpoints that never touch one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set this before the first <c>CreateClient()</c> call: that is what builds the host,
    /// and configuration is read once during the build. A settable property rather than a
    /// constructor parameter because xunit requires a class fixture to expose exactly one
    /// public constructor.
    /// </para>
    /// <para>
    /// Leaving it null is not "no database" — it is <see cref="NoDatabaseRequested"/>, a host
    /// that cannot resolve. Before step 52's follow-up, null meant falling through to
    /// <c>appsettings.json</c>, so a class that forgot to set this silently used the developer's
    /// own compose database: green wherever that happened to be running, and a 500 on CI, which
    /// is precisely how <c>AuthFlowTests</c> and <c>ErrorMappingFlowTests</c> went five steps
    /// without anyone noticing they had never been pointed anywhere.
    /// </para>
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
    /// <param name="technicianId">
    /// Which <c>Technician</c> this login is, for a test that needs a real one behind a
    /// <see cref="UserRole.Technician"/> token (step 50's sync endpoints). Ignored for the other
    /// two roles.
    /// </param>
    public async Task<AuthUser> SeedUserAsync(
        OrgId orgId,
        string username,
        string password,
        UserRole role,
        TechnicianId? technicianId = null)
    {
        var hasher = Services.GetRequiredService<IPasswordHasher>();
        var users = Services.GetRequiredService<IUserStore>();

        var user = new AuthUser(UserId.New(), orgId, username, hasher.Hash(password), role, technicianId);
        await users.AddAsync(user, CancellationToken.None).ConfigureAwait(false);

        return user;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        // Always overridden, never left to fall through: appsettings.json names a real host
        // (localhost:5433, the compose database a developer runs `make up` for), and a test that
        // reached it would be reading and writing real local data while looking like it passed.
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:ConnectionString"] = ConnectionString ?? NoDatabaseRequested,
            }));
    }
}
