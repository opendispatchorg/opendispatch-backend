using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Infrastructure.Provisioning;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Auth;

/// <summary>
/// How a deployment gets its first login — the work behind <c>create-user</c>.
/// </summary>
/// <remarks>
/// The verb itself is argument parsing and console output; what is worth testing is what it does to
/// the database, which is this. The bootstrap case is the first fact: a migrated database has no
/// organization either, so provisioning has to be able to register one and still refuse everything
/// that would produce a login answering for nobody.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class UserProvisioningTests
{
    private readonly PostgresFixture _postgres;

    public UserProvisioningTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task RegistersTheOrganizationWhenThereIsNotOneYet()
    {
        await using var services = BuildHost();
        var organization = $"Whitlock Heating {Guid.NewGuid():N}";
        var username = $"ada-{Guid.NewGuid():N}@whitlock.example";

        var (provisioned, refusal) = await ProvisionAsync(
            services, username, "riverside-heating", UserRole.Admin, organization);

        Assert.Equal(ProvisioningRefusal.None, refusal);
        Assert.NotNull(provisioned);
        Assert.True(provisioned.OrganizationCreated);
        Assert.False(provisioned.Replaced);

        // And the login signs in as that new tenant, which is the whole bootstrap: an organization
        // nobody can authenticate into is not a deployment anybody can use.
        var user = await FindAsync(services, username);
        Assert.NotNull(user);

        await using var context = _postgres.NewContext(user.OrgId);
        Assert.Equal(
            organization,
            await context.Organizations.Where(org => org.Id == user.OrgId).Select(org => org.Name).SingleAsync());
    }

    /// <summary>
    /// The second run against the same organization joins it rather than registering a second one
    /// under the same name.
    /// </summary>
    [Fact]
    public async Task JoinsAnOrganizationThatAlreadyExists()
    {
        await using var services = BuildHost();
        var organization = $"Vance Refrigeration {Guid.NewGuid():N}";

        var first = await ProvisionAsync(
            services, $"dana-{Guid.NewGuid():N}@vance.example", "correct-horse-battery", UserRole.Admin, organization);
        var second = await ProvisionAsync(
            services, $"priya-{Guid.NewGuid():N}@vance.example", "shift-plan-monday", UserRole.Dispatcher, organization);

        Assert.True(first.Provisioned!.OrganizationCreated);
        Assert.False(second.Provisioned!.OrganizationCreated);

        var dana = await FindAsync(services, first.Provisioned.Username);
        var priya = await FindAsync(services, second.Provisioned.Username);

        Assert.Equal(dana!.OrgId, priya!.OrgId);
    }

    /// <summary>
    /// Provisioning an existing username rewrites it — the only password reset this system has —
    /// and says so, because an operator who mistyped a username has just overwritten somebody.
    /// </summary>
    [Fact]
    public async Task RewritingAnExistingLoginReportsThatItReplacedOne()
    {
        await using var services = BuildHost();
        var organization = $"Riverside {Guid.NewGuid():N}";
        var username = $"sam-{Guid.NewGuid():N}@riverside.example";

        await ProvisionAsync(services, username, "first-password-here", UserRole.Dispatcher, organization);
        var again = await ProvisionAsync(services, username, "second-password-here", UserRole.Admin, organization);

        Assert.True(again.Provisioned!.Replaced);

        var user = await FindAsync(services, username);
        Assert.Equal(UserRole.Admin, user!.Role);

        using var scope = services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        Assert.True(hasher.Verify("second-password-here", user.PasswordHash));
        Assert.False(hasher.Verify("first-password-here", user.PasswordHash));
    }

    /// <summary>
    /// The same username under a different organization is refused, not moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Usernames are unique across the whole deployment, and <c>create-user</c> replaces an existing
    /// login rather than refusing it — a password reset is the only one this system has. Together
    /// those meant a mistyped <c>--org</c> silently <em>reassigned</em> somebody to another tenant
    /// and reported "replaced": one argument wrong, and a login crossed a tenant boundary with every
    /// query filter behind it dutifully following them across.
    /// </para>
    /// <para>
    /// The row must be untouched afterwards, not merely un-moved — a refusal that had already
    /// rewritten the password would be a failed command that changed something.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task RefusesAUsernameThatBelongsToAnotherOrganization()
    {
        await using var services = BuildHost();
        var username = $"sam-{Guid.NewGuid():N}@riverside.example";
        var here = $"Riverside {Guid.NewGuid():N}";
        var elsewhere = $"Lakeside {Guid.NewGuid():N}";

        await ProvisionAsync(services, username, "first-password-here", UserRole.Dispatcher, here);
        var theirs = await FindAsync(services, username);

        var moved = await ProvisionAsync(services, username, "second-password-here", UserRole.Admin, elsewhere);

        Assert.Equal(ProvisioningRefusal.UsernameBelongsToAnotherOrganization, moved.Refusal);
        Assert.Null(moved.Provisioned);

        // Still theirs, still their organization, still their password and their role.
        var after = await FindAsync(services, username);

        Assert.Equal(theirs!.OrgId, after!.OrgId);
        Assert.Equal(UserRole.Dispatcher, after.Role);

        using var scope = services.CreateScope();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        Assert.True(hasher.Verify("first-password-here", after.PasswordHash));
    }

    /// <summary>
    /// A technician login has to name a technician who works here. Without the check, the login
    /// authenticates and then answers for nobody — <c>/sync/pull</c> scopes to the technician on
    /// the token, so the technician's day would simply be empty forever.
    /// </summary>
    [Fact]
    public async Task RefusesATechnicianLoginForSomebodyElsesTechnician()
    {
        await using var services = BuildHost();
        var organization = $"Whitlock {Guid.NewGuid():N}";

        var (provisioned, refusal) = await ProvisionAsync(
            services,
            $"field-{Guid.NewGuid():N}@whitlock.example",
            "boiler-service-call",
            UserRole.Technician,
            organization,
            TechnicianId.New());

        Assert.Null(provisioned);
        Assert.Equal(ProvisioningRefusal.NoSuchTechnician, refusal);
    }

    [Fact]
    public async Task AcceptsATechnicianLoginForOneOfItsOwnTechnicians()
    {
        await using var services = BuildHost();
        var organization = $"Whitlock {Guid.NewGuid():N}";

        // The organization has to exist before its technician can, so the office login is what
        // registers it — which is the order a real deployment does this in too.
        var admin = await ProvisionAsync(
            services, $"ada-{Guid.NewGuid():N}@whitlock.example", "riverside-heating", UserRole.Admin, organization);

        var org = (await FindAsync(services, admin.Provisioned!.Username))!.OrgId;
        var technician = TechnicianBuilder.Any().ForOrg(org).Build();

        await using (var context = _postgres.NewContext(org))
        {
            context.Technicians.Add(technician);
            await context.SaveChangesAsync();
        }

        var (provisioned, refusal) = await ProvisionAsync(
            services,
            $"field-{Guid.NewGuid():N}@whitlock.example",
            "boiler-service-call",
            UserRole.Technician,
            organization,
            technician.Id);

        Assert.Equal(ProvisioningRefusal.None, refusal);
        Assert.Equal(technician.Id, (await FindAsync(services, provisioned!.Username))!.TechnicianId);
    }

    [Fact]
    public async Task RefusesATechnicianOnAnOfficeLogin()
    {
        await using var services = BuildHost();

        var (provisioned, refusal) = await ProvisionAsync(
            services,
            $"dana-{Guid.NewGuid():N}@vance.example",
            "correct-horse-battery",
            UserRole.Dispatcher,
            $"Vance {Guid.NewGuid():N}",
            TechnicianId.New());

        Assert.Null(provisioned);
        Assert.Equal(ProvisioningRefusal.TechnicianOnAnOfficeLogin, refusal);
    }

    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);

    private static async Task<(ProvisionedUser? Provisioned, ProvisioningRefusal Refusal)> ProvisionAsync(
        IServiceProvider services,
        string username,
        string password,
        UserRole role,
        string organization,
        TechnicianId? technician = null)
    {
        using var scope = services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<UserProvisioner>()
            .ProvisionAsync(username, password, role, organization, technician, CancellationToken.None);
    }

    private static async Task<AuthUser?> FindAsync(IServiceProvider services, string username)
    {
        using var scope = services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IUserStore>()
            .FindByUsernameAsync(username, CancellationToken.None);
    }
}
