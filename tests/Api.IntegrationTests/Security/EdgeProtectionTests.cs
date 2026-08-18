using System.Net;
using System.Net.Http.Json;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts.Auth;
using OpenDispatch.Contracts.Schedule;
using OpenDispatch.Contracts.Sync;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Security;

/// <summary>
/// What stands between this API and a caller — invited or not — spending more of it than they
/// should: a cap on sign-in attempts, caps on the two expensive authenticated paths, and an origin
/// allow-list.
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

    private readonly PostgresFixture _postgres;

    /// <remarks>
    /// The fixture is here for one fact only — the rate-limit test seeds a login, and a login is a
    /// row in the users table now rather than an entry in a dictionary. The CORS facts below still
    /// need no database and are not given one.
    /// </remarks>
    public EdgeProtectionTests(PostgresFixture postgres) => _postgres = postgres;

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
            ConnectionString = _postgres.ConnectionString,
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

    /// <summary>
    /// The two authenticated paths that are expensive enough to cap, and the thing that makes them
    /// different from the login limit: the partition is <em>who is calling</em>, not where from.
    /// </summary>
    /// <remarks>
    /// Neither limit is about an attacker. A phone stuck in a retry loop and a browser with a
    /// wedged refresh are ordinary accidents, and both can spend a shop's database on nothing. An
    /// office of dispatchers behind one address is not one caller, which is why partitioning by
    /// address — right for login, where nobody is signed in yet — would be wrong here.
    /// </remarks>
    [Fact]
    public async Task AnOptimiseLoopIsCappedPerOrganization()
    {
        await using var factory = new ApiFactory
        {
            ConnectionString = _postgres.ConnectionString,
            Settings =
            {
                ["RateLimit:Enabled"] = "true",
                ["RateLimit:OptimizationsPerMinute"] = "1",
            },
        };

        var org = OrgId.New();
        var username = $"dispatch-{Guid.NewGuid():N}@whitlock.example";
        await factory.SeedUserAsync(org, username, "riverside-heating", UserRole.Dispatcher);

        using var client = factory.CreateClient();
        var token = await client.LoginAsync(username, "riverside-heating");

        var day = new DateTimeOffset(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);
        var body = new OptimizeScheduleRequest(day.AddHours(6), day.AddHours(20), null);

        using var first = await SendAsync(client, token, "/schedule/optimize", body);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var second = await SendAsync(client, token, "/schedule/optimize", body);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task APhoneStuckInAPushLoopIsCappedPerTechnician()
    {
        await using var factory = new ApiFactory
        {
            ConnectionString = _postgres.ConnectionString,
            Settings =
            {
                ["RateLimit:Enabled"] = "true",
                ["RateLimit:PushesPerMinute"] = "1",
            },
        };

        var org = OrgId.New();
        var username = $"field-{Guid.NewGuid():N}@whitlock.example";
        await factory.SeedUserAsync(org, username, "boiler-service", UserRole.Technician, TechnicianId.New());

        using var client = factory.CreateClient();
        var token = await client.LoginAsync(username, "boiler-service");

        // An empty batch: this is about the limiter, not about what a push does, and an empty one
        // is a request a real device makes when it has nothing to say but syncs anyway.
        var body = new SyncPushRequest([]);

        using var first = await SendAsync(client, token, "/sync/push", body);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, first.StatusCode);

        using var second = await SendAsync(client, token, "/sync/push", body);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, string token, string path, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path).Authorized(token);
        request.Content = JsonContent.Create(body);

        return client.SendAsync(request);
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
