using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Auth;

/// <summary>
/// Logins are rows now, and the three facts that follow from that.
/// </summary>
/// <remarks>
/// <para>
/// The store this replaces was a process-lifetime dictionary, which is why these tests are new
/// rather than moved: none of what they assert was true of it, and the first — that a user outlives
/// the process — was exactly the thing a deployment could not have.
/// </para>
/// <para>
/// Against a real database, because the interesting half is the database's: the uniqueness is an
/// index, and the lookup deliberately reads past the tenant filter every other table is scoped by.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class UserStoreTests
{
    private readonly PostgresFixture _postgres;

    public UserStoreTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>
    /// The whole point of the change: a login written by one process is there for the next one.
    /// </summary>
    [Fact]
    public async Task AUserOutlivesTheHostThatCreatedThem()
    {
        var org = OrgId.New();
        var username = $"ada-{Guid.NewGuid():N}@whitlock.example";

        await using (var writing = TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true))
        {
            await AddAsync(writing, new AuthUser(
                UserId.New(), org, username, Hash(writing, "riverside-heating"), UserRole.Admin));
        }

        // A different container, a different context, the same database — which is what a restart
        // is, and what the in-memory store could never survive.
        await using var reading = TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);
        var found = await FindAsync(reading, username);

        Assert.NotNull(found);
        Assert.Equal(org, found.OrgId);
        Assert.Equal(UserRole.Admin, found.Role);
    }

    /// <summary>
    /// The port promises a case-insensitive lookup; a unique index compares bytes. Normalizing on
    /// the way in is what makes those the same promise.
    /// </summary>
    [Fact]
    public async Task FindsAUserWhateverCaseTheyType()
    {
        await using var services = TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);
        var username = $"Priya-{Guid.NewGuid():N}@Vance.Example";

        await AddAsync(services, new AuthUser(
            UserId.New(), OrgId.New(), username, Hash(services, "shift-plan-monday"), UserRole.Dispatcher));

        Assert.NotNull(await FindAsync(services, username.ToUpperInvariant()));
        Assert.NotNull(await FindAsync(services, username.ToLowerInvariant()));
        Assert.NotNull(await FindAsync(services, $"  {username}  "));
    }

    /// <summary>
    /// Seeding the same username twice rewrites the one user rather than adding a second — what
    /// the port means by "adds a user, or replaces the one already seeded", and what the demo
    /// registrar does on every development start.
    /// </summary>
    [Fact]
    public async Task RegisteringTheSameUsernameTwiceRewritesTheOneUser()
    {
        await using var services = TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);
        var org = OrgId.New();
        var username = $"sam-{Guid.NewGuid():N}@vance.example";

        await AddAsync(services, new AuthUser(
            UserId.New(), org, username, Hash(services, "first-password-here"), UserRole.Technician));
        await AddAsync(services, new AuthUser(
            UserId.New(), org, username, Hash(services, "second-password-here"), UserRole.Admin));

        var normalized = username.ToLowerInvariant();

        await using var context = _postgres.NewContext(org);
        var rows = await context.Users.IgnoreQueryFilters()
            .Where(user => user.Username == normalized)
            .ToListAsync();

        var only = Assert.Single(rows);
        Assert.Equal(UserRole.Admin, only.Role);

        // The rewritten row is the same user: an id that changed underneath a token would log
        // somebody out of a session they still hold.
        var found = await FindAsync(services, username);
        Assert.Equal(only.Id, found!.Id);
    }

    private static string Hash(IServiceProvider services, string password)
    {
        using var scope = services.CreateScope();

        return scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(password);
    }

    private static async Task AddAsync(IServiceProvider services, AuthUser user)
    {
        using var scope = services.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IUserStore>()
            .AddAsync(user, CancellationToken.None);
    }

    private static async Task<AuthUser?> FindAsync(IServiceProvider services, string username)
    {
        using var scope = services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IUserStore>()
            .FindByUsernameAsync(username, CancellationToken.None);
    }
}
