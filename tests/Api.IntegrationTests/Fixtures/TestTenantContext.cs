using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Api.IntegrationTests.Fixtures;

/// <summary>
/// The tenant, as a test states it.
/// </summary>
/// <remarks>
/// Stands in for the middleware that will resolve a real one from the authenticated principal
/// (step 45). It is scoped like the real thing and settable rather than fixed, because the tests
/// that matter here are the ones where two organizations are live in the same process: a fixed
/// tenant per provider could not express "org A cannot see org B's rows" at all.
/// </remarks>
internal sealed class TestTenantContext : ITenantContext
{
    private OrgId? _orgId;

    public OrgId OrgId => _orgId ?? throw new InvalidOperationException(
        "This test did not say which tenant it was acting as.");

    internal void ActAs(OrgId orgId) => _orgId = orgId;
}
