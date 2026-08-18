using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Security;

/// <summary>
/// Every route this host serves says who may call it — and the ones anybody may call say so out
/// loud.
/// </summary>
/// <remarks>
/// <para>
/// The rule nothing else enforces. ASP.NET Core's default for an endpoint with no authorization
/// metadata is <em>anonymous</em>, so a route mapped without <c>RequireAuthorization</c> is public
/// and nothing anywhere complains: not the compiler, not a review that did not happen to open that
/// file, not a test of the feature, which will be written holding a token anyway. The failure is
/// silent by construction, which is what makes it worth a test rather than a convention.
/// </para>
/// <para>
/// A fallback policy would enforce it in the product instead, and was tried: ASP.NET Core applies
/// one to requests matching <em>no</em> endpoint too, so every mistyped URL becomes a 401 and step
/// 46's answer for an unknown path stops being a 404. This walks the host's own endpoint list
/// instead, which costs the product nothing and fails in CI rather than in production.
/// </para>
/// <para>
/// The anonymous list is written out rather than counted, so adding a public route is a deliberate
/// edit to a test that names it — the same shape as <c>SchemaTests</c>' list of tables and
/// <c>PortTests</c>' rules about the ports.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class EndpointAuthorizationTests
{
    /// <summary>
    /// The routes a caller with no token may reach: signing in, the three probes an orchestrator
    /// runs, and the two development-only fixtures.
    /// </summary>
    private static readonly string[] PublicRoutes =
    [
        "/auth/login",
        "/health",
        "/health/live",
        "/health/ready",
        "/_diagnostics/throws",
        "/openapi/{documentName}.json",
    ];

    [Fact]
    public void EveryRouteEitherAsksForAPolicyOrSaysItIsPublic()
    {
        using var undecided = Routes()
            .Where(route => !Requires(route) && !Anonymous(route))
            .GetEnumerator();

        Assert.False(
            undecided.MoveNext(),
            undecided.Current is { } route
                ? $"'{route.RoutePattern.RawText}' carries neither an authorization policy nor "
                    + "AllowAnonymous, so ASP.NET Core will serve it to anybody."
                : string.Empty);
    }

    [Fact]
    public void OnlyTheRoutesThatShouldBePublicAreAnonymous()
    {
        var anonymous = Routes()
            .Where(Anonymous)
            .Select(route => "/" + route.RoutePattern.RawText!.TrimStart('/'))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(PublicRoutes.Order(StringComparer.Ordinal), anonymous);
    }

    /// <remarks>
    /// The real host, in Development so the diagnostics and OpenAPI routes it maps only there are
    /// part of what is inspected — the environment with the largest public surface is the one worth
    /// asserting about.
    /// </remarks>
    private static List<RouteEndpoint> Routes()
    {
        using var factory = new ApiFactory { Environment = "Development" };

        return factory.Services
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>()
            .ToList();
    }

    private static bool Requires(Endpoint route) =>
        route.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;

    private static bool Anonymous(Endpoint route) =>
        route.Metadata.GetOrderedMetadata<IAllowAnonymous>().Count > 0;
}
