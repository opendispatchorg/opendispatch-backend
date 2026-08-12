namespace OpenDispatch.Infrastructure.Auth;

/// <summary>The custom claim type names on an OpenDispatch JWT.</summary>
/// <remarks>
/// Short names, not the <c>System.Security.Claims.ClaimTypes.*</c> URIs — a client decoding a
/// token to show role-gated UI without a round trip reads <c>"role"</c> more easily than
/// <c>"http://schemas.microsoft.com/ws/2008/06/identity/claims/role"</c> as a JSON key, and
/// nothing in Document 2 requires the long form. <see cref="Role"/> only satisfies ASP.NET
/// Core's <c>[Authorize(Roles = ...)]</c> because Program.cs sets
/// <c>TokenValidationParameters.RoleClaimType</c> to match; the two must agree, and this
/// constant is what keeps them agreeing.
/// </remarks>
public static class AuthClaimTypes
{
    /// <summary>The organization (<c>OrgId</c>) the caller belongs to. Step 45 reads this.</summary>
    public const string Org = "org";

    /// <summary>The caller's role — see <see cref="OpenDispatch.Application.Auth.UserRole"/>.</summary>
    public const string Role = "role";

    /// <summary>
    /// Which technician the caller is. Present only on a token issued for a
    /// <see cref="OpenDispatch.Application.Auth.UserRole.Technician"/> whose <c>AuthUser</c> was
    /// seeded with one. Step 50 reads this — the sync endpoints are the first thing that needs to
    /// know whose field work a request is about.
    /// </summary>
    public const string Technician = "tech";
}
