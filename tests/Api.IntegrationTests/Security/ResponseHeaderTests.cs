using System.Net;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Security;

/// <summary>
/// What every response tells a browser about what it may do with it.
/// </summary>
/// <remarks>
/// <para>
/// Until this landed the only security header anywhere in this host was the <c>nosniff</c> that
/// <c>GET /attachments/{id}/content</c> set for itself. That is the one path that genuinely serves
/// bytes a stranger uploaded, so it was the right first one — but an API reached from two browser
/// applications, which can be framed, linked from and sniffed like anything else, had nothing.
/// </para>
/// <para>
/// The failure case is asserted beside the success case on purpose. Headers set inline are cleared
/// by <c>UseExceptionHandler</c> before it writes its 500 — the defect <c>CorrelationIdMiddleware</c>
/// already found the hard way — so "every response" has to include the ones nothing went right in.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class ResponseHeaderTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public ResponseHeaderTests(ApiFactory factory) => _factory = factory;

    [Theory]
    // A 200 from a route that needs nothing, a 404 from a route that does not exist, and a 401
    // from one that does and was not given a token — three different writers of a response.
    [InlineData("/health", HttpStatusCode.OK)]
    [InlineData("/no-such-route", HttpStatusCode.NotFound)]
    [InlineData("/jobs", HttpStatusCode.Unauthorized)]
    public async Task EveryResponseCarriesTheSecurityHeaders(string path, HttpStatusCode expected)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(expected, response.StatusCode);

        Assert.Equal("nosniff", Single(response, "X-Content-Type-Options"));
        Assert.Equal("DENY", Single(response, "X-Frame-Options"));
        Assert.Equal("frame-ancestors 'none'", Single(response, "Content-Security-Policy"));
        Assert.Equal("no-referrer", Single(response, "Referrer-Policy"));
        Assert.Contains("camera=()", Single(response, "Permissions-Policy"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The one header that must not be sent unconditionally.
    /// </summary>
    /// <remarks>
    /// <c>Strict-Transport-Security</c> tells a browser never to reach this origin over plain HTTP
    /// again, for a year, and no server-side change undoes it. Sent from a host being reached over
    /// HTTP — a developer's laptop, a shop on an internal network — it locks them out of their own
    /// system. So it is conditional on the request actually having arrived over TLS, which under
    /// this in-memory server it never does.
    /// </remarks>
    [Fact]
    public async Task DoesNotSendHstsOverPlainHttp()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.False(
            response.Headers.Contains("Strict-Transport-Security"),
            "a host reached over plain HTTP told the browser never to use plain HTTP again.");
    }

    private static string Single(HttpResponseMessage response, string header) =>
        response.Headers.TryGetValues(header, out var values)
            ? Assert.Single(values)
            : Assert.Single(response.Content.Headers.GetValues(header));
}
