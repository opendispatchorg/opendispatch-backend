namespace OpenDispatch.Application.Auth;

/// <summary>
/// The three roles the system knows, exactly as Document 2 §7 names them.
/// </summary>
/// <remarks>
/// Not a domain concept. Nothing in <c>Domain</c> enforces a rule that depends on who is
/// logged in — a technician's own aggregate has no idea a login exists, and Document 2 §3's
/// list of aggregate roots has no room for one. This is the shape of who may call the API,
/// which is Application/Infrastructure/Api's business, the same reasoning that keeps
/// <see cref="AuthUser"/> and (from step 41) <c>SyncOpRecord</c> out of the domain.
/// </remarks>
public enum UserRole
{
    /// <summary>Runs the business: everything, including the other two roles' work.</summary>
    Admin,

    /// <summary>Books, schedules and dispatches work — the board's operator.</summary>
    Dispatcher,

    /// <summary>Works the jobs assigned to them, mostly from the field app.</summary>
    Technician,
}
