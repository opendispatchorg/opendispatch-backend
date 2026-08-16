using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Auth;

/// <summary>
/// Somebody who can call the API — the minimal user store step 44 asks for, and every field a
/// login needs to answer "who is this and what may they do".
/// </summary>
/// <remarks>
/// Not an aggregate — see <see cref="UserRole"/>'s remarks. It carries an <see cref="OrgId"/>
/// rather than resolving one through <c>ITenantContext</c> because logging in is how a caller
/// gets a tenant in the first place; there is no ambient scope yet for this one request to read.
/// </remarks>
/// <param name="Id">This user's identity.</param>
/// <param name="OrgId">The tenant they sign in as.</param>
/// <param name="Username">What they type into the login form. Looked up case-insensitively.</param>
/// <param name="PasswordHash">Never the password itself — see <c>IPasswordHasher</c>.</param>
/// <param name="Role">What they are allowed to do.</param>
/// <param name="IsActive">
/// Whether they may still sign in. The one thing this system has that resembles a user lifecycle:
/// <c>create-user</c> was the only way in and there was no way out, so a dismissed employee kept a
/// working login until somebody deleted a row by hand. Checked at login, which means an existing
/// token outlives the change by up to <c>Jwt:ExpiryMinutes</c> — see the README.
/// </param>
/// <param name="TechnicianId">
/// Which <c>Technician</c> this login is, for a <see cref="UserRole.Technician"/> — <see
/// langword="null"/> for the other two roles, which are not anybody's field identity. Step 42 left
/// this unresolved on purpose ("there is no user-to-technician mapping until auth"); step 50 is the
/// first caller that needs one, to know whose sync scope a request is asking for. Not enforced here
/// — <see cref="AuthUser"/> is not an aggregate and validates nothing about itself — so a
/// technician seeded without one is a seeding mistake the sync endpoints report as a bare 401
/// rather than something this type refuses to construct.
/// </param>
public sealed record AuthUser(
    UserId Id,
    OrgId OrgId,
    string Username,
    string PasswordHash,
    UserRole Role,
    bool IsActive = true,
    TechnicianId? TechnicianId = null);
