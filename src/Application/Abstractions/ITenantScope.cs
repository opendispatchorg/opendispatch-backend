using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Establishes whose work a unit of work is for.
/// </summary>
/// <remarks>
/// <para>
/// The write half of <see cref="ITenantContext"/>, kept separate so that reading the tenant and
/// <em>deciding</em> it are different capabilities. Everything in the system takes
/// <c>ITenantContext</c> and can only read; the two things entitled to say who a scope belongs to
/// take this instead.
/// </para>
/// <para>
/// There are exactly two: the API's tenant-resolution middleware, which reads the org claim off an
/// authenticated principal, and the outbox dispatcher, which is not serving a request at all and
/// resolves the owner recorded on each message. The second is why this interface exists — a
/// background sweep that could not establish a tenant published to subscribers that all read one,
/// so every delivery threw and the outbox stopped being a safety net.
/// </para>
/// <para>
/// An implementation refuses a second resolve. A scope whose tenant changed halfway through would
/// mean the queries before the change and the queries after it read different organizations, which
/// is the exact failure the query filters exist to make impossible.
/// </para>
/// </remarks>
public interface ITenantScope
{
    /// <summary>Records whose work this scope is for.</summary>
    /// <param name="orgId">The organization.</param>
    /// <exception cref="InvalidOperationException">A tenant has already been resolved for this scope.</exception>
    void Resolve(OrgId orgId);
}
