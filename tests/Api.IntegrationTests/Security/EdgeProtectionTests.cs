using System.Net;
using System.Net.Http.Json;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts.Auth;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Security;

/// <summary>
/// The two things standing between this API and a browser or a script it did not invite: a cap on
/// sign-in attempts, and an origin allow-list.
/// </summary>
/// <remarks>
/// Both are configuration-driven and both do nothing by default, so each fact boots a host
/// configured the way a deployment would configure it. That is the only way to test either one:
/// the shared factory deliberately disables the limiter and configures no origins, because the
/// suite as a whole is the one caller that looks like an attack.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class EdgeProtectionTests
{
    private const string Origin = "https://board.opendispatch.example";

    /// <summary>
    /// Guessing stops after the configured number of attempts, with the same ProblemDetails shape
    /// every other refusal in this API uses — and the refusal is about the address, not the
    /// account, so it holds for a guesser working through a list of usernames.
    /// </summary>
    [Fact]
    public async Task SignInAttemptsAreCappedPerAddress()
    {
        await using var factory = new ApiFactory
        {
            Settings =
            {
                ["RateLimit:Enabled"] = "true",
                ["RateLimit:PermitLimit"] = "2",
                ["RateLimit:WindowSeconds"] = "300",
            },
        };

        await factory.SeedUserAsync(OrgId.New(), "ada@whitlock.example", "riverside-heating", UserRole.Admin);
        using var client = factory.CreateClient();

        // Two attempts, both answered on their merits: one wrong, one right.
        using var wrong = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("ada@whitlock.example", "not-the-password"));
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        using var right = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("ada@whitlock.example", "riverside-heating"));
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);

        // The third is refused before the password is ever verified — including this one, which
        // would otherwise succeed.
        using var capped = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("ada@whitlock.example", "riverside-heating"));

        Assert.Equal(HttpStatusCode.TooManyRequests, capped.StatusCode);
        Assert.Equal("application/problem+json", capped.Content.Headers.ContentType?.MediaType);
        Assert.NotNull(capped.Headers.RetryAfter);
    }

    /// <summary>
    /// Nothing else is capped: the limiter is a policy on one route, not a global throttle, so an
    /// office board polling or a fleet of phones syncing is untouched.
    /// </summary>
    [Fact]
    public async Task NothingButSignInIsCapped()
    {
        await using var factory = new ApiFactory
        {
            Settings =
            {
                ["RateLimit:Enabled"] = "true",
                ["RateLimit:PermitLimit"] = "1",
            },
        };

        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var response = await client.GetAsync("/health/live");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task AConfiguredBrowserOriginIsAllowedAndOthersAreNot()
    {
        await using var factory = new ApiFactory { Settings = { ["Cors:Origins:0"] = Origin } };
        using var client = factory.CreateClient();

        using var allowed = await PreflightAsync(client, Origin);

        Assert.Equal(
            Origin,
            Assert.Single(allowed.Headers.GetValues("Access-Control-Allow-Origin")));

        using var refused = await PreflightAsync(client, "https://not-our-board.example");

        // No header rather than an error status: that is how CORS refuses, and it is the browser
        // that then blocks the call.
        Assert.False(refused.Headers.Contains("Access-Control-Allow-Origin"));
    }

    /// <summary>
    /// A host that has configured no origins registers no CORS middleware at all — the demo and
    /// every non-browser caller are unchanged by this feature existing.
    /// </summary>
    [Fact]
    public async Task NoConfiguredOriginsMeansNoCorsHeaders()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        using var response = await PreflightAsync(client, Origin);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static Task<HttpResponseMessage> PreflightAsync(HttpClient client, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/jobs");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        return client.SendAsync(request);
    }
}
