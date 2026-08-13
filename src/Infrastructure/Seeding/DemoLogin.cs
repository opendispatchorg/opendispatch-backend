using OpenDispatch.Application.Auth;

namespace OpenDispatch.Infrastructure.Seeding;

/// <summary>
/// One set of demo credentials: what to type in, and who it turns out to be.
/// </summary>
/// <remarks>
/// <para>
/// The password is in the clear because the whole point of a demo login is that somebody who has
/// just cloned the repository can read it and use it. Nothing here is a secret being leaked: the
/// seeder these belong to refuses to run outside Development (see <see cref="DemoSeeding"/>), and
/// these accounts exist nowhere else.
/// </para>
/// <para>
/// There are three rather than the one Document 3 asks for, because one cannot demonstrate the
/// system: the role policies split the surface three ways, so an Admin token cannot call
/// <c>/sync</c> and a Technician token cannot see the dispatch board. Three is the smallest set
/// that can walk the whole loop.
/// </para>
/// </remarks>
/// <param name="Username">What goes in the login form. Looked up case-insensitively.</param>
/// <param name="Password">The plaintext password, hashed on the way into the user store.</param>
/// <param name="Role">What this login is allowed to do.</param>
/// <param name="TechnicianName">
/// Which seeded technician this login *is*, for <see cref="UserRole.Technician"/> — the field
/// <c>/sync</c> scopes a request by. <see langword="null"/> for the office roles, which are not
/// anybody's field identity.
/// </param>
public sealed record DemoLogin(string Username, string Password, UserRole Role, string? TechnicianName);
