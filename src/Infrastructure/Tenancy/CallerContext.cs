using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Auth;

namespace OpenDispatch.Infrastructure.Tenancy;

/// <summary>
/// Who is making this request, once something has resolved them.
/// </summary>
/// <remarks>
/// <para>
/// Scoped, and filled in by the same middleware that resolves the tenant — from the same principal,
/// which already carries both. It is the answer to "who did this" that the audit trail records.
/// </para>
/// <para>
/// <strong>Unresolved is legitimate here, unlike the tenant's.</strong> A sign-in has no caller yet,
/// the outbox sweep has none at all, and neither does a CLI verb. So this reports null rather than
/// throwing — and an audit entry with no actor means the system acted on its own, which is a fact
/// worth being able to tell apart from a person doing something.
/// </para>
/// <para>
/// Public for the same reason <see cref="TenantContext"/> is: <see cref="Resolve"/> has no home on
/// the port without giving <c>ICallerContext</c> a write side that any handler could reach.
/// </para>
/// </remarks>
public sealed class CallerContext : ICallerContext
{
    /// <inheritdoc />
    public UserId? UserId { get; private set; }

    /// <inheritdoc />
    public string? Username { get; private set; }

    /// <summary>Records whose request this is.</summary>
    /// <param name="userId">The signed-in user.</param>
    /// <param name="username">What they signed in as.</param>
    /// <exception cref="InvalidOperationException">
    /// A caller was already resolved. Changing it mid-request would mean the audit entries written
    /// before the change name a different person from the ones after it.
    /// </exception>
    public void Resolve(UserId userId, string? username)
    {
        if (UserId is not null)
        {
            throw new InvalidOperationException("The caller for this request has already been resolved.");
        }

        UserId = userId;
        Username = username;
    }
}
