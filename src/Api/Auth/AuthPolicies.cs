namespace OpenDispatch.Api.Auth;

/// <summary>Named authorization policies, one per role (Document 3, step 44).</summary>
/// <remarks>
/// Named rather than inlined everywhere as <c>[Authorize(Roles = "Admin")]</c>: the build plan
/// asks for policies, and a typo in a role literal fails silently — an unmatched role simply
/// authorizes nobody — where a typo in a constant name fails to compile.
/// </remarks>
public static class AuthPolicies
{
    /// <summary>Admins only.</summary>
    public const string AdminOnly = "AdminOnly";

    /// <summary>Dispatchers only.</summary>
    public const string DispatcherOnly = "DispatcherOnly";

    /// <summary>Technicians only.</summary>
    public const string TechnicianOnly = "TechnicianOnly";

    /// <summary>
    /// Admins or Dispatchers — the two office-side roles, step 47's default for the
    /// Customers/Technicians/Jobs endpoints they both use day to day. Technicians reach this
    /// data through the sync endpoints (step 50) instead, never through this surface.
    /// </summary>
    public const string AdminOrDispatcher = "AdminOrDispatcher";
}
