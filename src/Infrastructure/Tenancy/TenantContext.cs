using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Infrastructure.Tenancy;

/// <summary>
/// The tenant for one request, once something has resolved it.
/// </summary>
/// <remarks>
/// <para>
/// Scoped, so it is per-request state and nothing more. <c>TenantResolutionMiddleware</c> (Api,
/// step 45) fills it in from the authenticated principal's org claim; a request nothing resolves
/// a tenant for — genuinely anonymous, an <c>AllowAnonymous</c> endpoint — leaves it unresolved,
/// and that is visible rather than silent: reading <see cref="OrgId"/> unresolved throws.
/// </para>
/// <para>
/// Throwing is the whole point. The alternative, handing back a default <see cref="OrgId"/>, makes
/// every tenant-scoped query match nothing: no error, no leak, and a dispatch board that is
/// mysteriously empty. An exception names the actual problem — that this code path never
/// established who it was working for.
/// </para>
/// <para>
/// Public rather than internal, and this is the one exception to "Api reaches into
/// Infrastructure only for DI registration" (Document 2 §2): <see cref="Resolve"/> has no home
/// in <c>Application.Abstractions</c> without giving <c>ITenantContext</c> a write side, which
/// would break the one-port-mentions-<c>OrgId</c> rule <c>PortTests</c> enforces and turn tenant
/// scope back into something a second port could get wrong. The middleware that calls it is
/// still composition-root-adjacent code — request-pipeline wiring, not a controller — so nothing
/// that depends on <c>Application</c> abstractions needs to know this type exists.
/// </para>
/// </remarks>
public sealed class TenantContext : ITenantContext
{
    private OrgId? _orgId;

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">Nothing has resolved a tenant for this request.</exception>
    public OrgId OrgId => _orgId ?? throw new InvalidOperationException(
        "No tenant has been resolved for this request, so there is no organization to scope it to.");

    /// <summary>Records whose request this is.</summary>
    /// <exception cref="InvalidOperationException">
    /// A tenant was already resolved. Changing it mid-request would mean the queries before the
    /// change and the queries after it read different organizations, which is the exact failure
    /// the query filters exist to make impossible.
    /// </exception>
    public void Resolve(OrgId orgId)
    {
        if (_orgId is not null)
        {
            throw new InvalidOperationException("The tenant for this request has already been resolved.");
        }

        _orgId = orgId;
    }
}
