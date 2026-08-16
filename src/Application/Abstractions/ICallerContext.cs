using OpenDispatch.Application.Auth;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Who is making this request.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="ITenantContext"/> and deliberately separate from it: the tenant is
/// what a request may <em>see</em>, and every read in the system is scoped by it; the caller is who
/// is answerable for what a request <em>did</em>, and nothing reads by it. Fusing them would tie the
/// audit trail to the query filters, and a change to either would be a change to both.
/// </para>
/// <para>
/// <strong>Unresolved is a legitimate state, unlike the tenant's.</strong> A login has no caller
/// yet; the outbox sweep and the CLI verbs have no caller at all. So this reports null rather than
/// throwing, and the audit trail records what it was told — an entry with no actor is a fact about
/// the system acting on its own, which is worth being able to tell from a person doing something.
/// </para>
/// </remarks>
public interface ICallerContext
{
    /// <summary>The signed-in user, or <see langword="null"/> when nobody is signed in.</summary>
    UserId? UserId { get; }

    /// <summary>What they signed in as, or <see langword="null"/>.</summary>
    string? Username { get; }
}
