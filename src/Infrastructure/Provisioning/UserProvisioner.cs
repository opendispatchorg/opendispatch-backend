using Microsoft.EntityFrameworkCore;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Organizations;
using OpenDispatch.Infrastructure.Persistence;

namespace OpenDispatch.Infrastructure.Provisioning;

/// <summary>
/// What happened to a provisioning request.
/// </summary>
/// <param name="Username">The username as it was stored — normalized, which may not be what was typed.</param>
/// <param name="Organization">The tenant the login belongs to.</param>
/// <param name="OrganizationCreated">Whether that tenant had to be registered first.</param>
/// <param name="Role">What the login may do.</param>
/// <param name="Replaced">Whether a login already existed under this username and was rewritten.</param>
public sealed record ProvisionedUser(
    string Username,
    string Organization,
    bool OrganizationCreated,
    UserRole Role,
    bool Replaced);

/// <summary>
/// Why a provisioning request was refused.
/// </summary>
public enum ProvisioningRefusal
{
    /// <summary>Nothing was refused.</summary>
    None,

    /// <summary>The named technician is not in this organization, or does not exist.</summary>
    NoSuchTechnician,

    /// <summary>A technician was named for a login that is not a technician's.</summary>
    TechnicianOnAnOfficeLogin,
}

/// <summary>
/// Creates the first administrator a deployment signs in as — and every one after them.
/// </summary>
/// <remarks>
/// <para>
/// The bootstrap gap this closes: a freshly migrated database has no organization and no users, and
/// nothing in the HTTP surface creates either (no registration endpoint, by design — Document 3
/// names no step that manages users). Without this, a deployment is a running host nobody can log
/// into.
/// </para>
/// <para>
/// It lives in Infrastructure rather than in the Api verb that calls it for the same reason
/// <see cref="Seeding.DemoSeeder"/> does: the verb is a shell around work that needs the context,
/// the hasher and the store, and a second copy of "find or create the organization" is a second
/// chance to get tenancy wrong. Unlike the seeder it is registered in <em>every</em> environment —
/// creating an administrator is exactly what a production host needs to do once.
/// </para>
/// <para>
/// <strong>Provisioning an existing username rewrites it</strong>, which is deliberate and is the
/// only password reset this system has. It is the same upsert <c>IUserStore.AddAsync</c> promises;
/// the caller is told which of the two happened so it can say so.
/// </para>
/// <para>
/// Reads ignore the tenant filter and name their organization explicitly, exactly as the seeder's
/// do: this runs outside any request, so nothing has resolved a tenant and a filtered query would
/// throw before it could find the organization it is about to write into.
/// </para>
/// </remarks>
public sealed class UserProvisioner(AppDbContext database, IUserStore users, IPasswordHasher passwords)
{
    /// <summary>
    /// Creates or rewrites a login, registering its organization if that does not exist yet.
    /// </summary>
    /// <param name="username">What they will type. Stored normalized; unique across the deployment.</param>
    /// <param name="password">The plaintext password, hashed here and never stored as given.</param>
    /// <param name="role">What the login may do.</param>
    /// <param name="organizationName">The tenant, by name. Registered if no tenant has that name.</param>
    /// <param name="technician">
    /// Which technician this login is, for a <see cref="UserRole.Technician"/>. Checked against the
    /// organization, because a technician login pointing at somebody else's technician — or at
    /// nobody — is a sync scope that quietly answers for the wrong person.
    /// </param>
    /// <param name="ct">Cancels the work.</param>
    /// <returns>What was done, or why it was refused.</returns>
    public async Task<(ProvisionedUser? Provisioned, ProvisioningRefusal Refusal)> ProvisionAsync(
        string username,
        string password,
        UserRole role,
        string organizationName,
        TechnicianId? technician,
        CancellationToken ct)
    {
        if (technician is not null && role is not UserRole.Technician)
        {
            return (null, ProvisioningRefusal.TechnicianOnAnOfficeLogin);
        }

        var organization = await database.Organizations
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Name == organizationName.Trim(), ct)
            .ConfigureAwait(false);

        var registered = organization is null;

        if (organization is null)
        {
            organization = Organization.Create(organizationName);
            database.Organizations.Add(organization);
            await database.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (technician is { } field && !await BelongsAsync(field, organization.Id, ct).ConfigureAwait(false))
        {
            return (null, ProvisioningRefusal.NoSuchTechnician);
        }

        var replacing = await users.FindByUsernameAsync(username, ct).ConfigureAwait(false);

        await users.AddAsync(
            new AuthUser(
                UserId.New(),
                organization.Id,
                username,
                passwords.Hash(password),
                role,
                IsActive: true,
                technician),
            ct).ConfigureAwait(false);

        return (
            new ProvisionedUser(
                username.Trim().ToLowerInvariant(),
                organization.Name,
                registered,
                role,
                replacing is not null),
            ProvisioningRefusal.None);
    }

    private Task<bool> BelongsAsync(TechnicianId technician, OrgId organization, CancellationToken ct) =>
        database.Technicians
            .IgnoreQueryFilters()
            .AnyAsync(candidate => candidate.Id == technician && candidate.OrgId == organization, ct);
}
