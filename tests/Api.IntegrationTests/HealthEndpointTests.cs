using System.Net;
using System.Net.Http.Json;
using OpenDispatch.Api.Health;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Infrastructure.Persistence;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests;

/// <summary>
/// Boots the real host through <see cref="ApiFactory"/>, so this exercises configuration
/// binding, startup validation and routing end to end rather than calling the endpoint
/// delegate directly.
/// </summary>
/// <remarks>
/// <para>
/// The class fixture is deliberately left with no <see cref="ApiFactory.ConnectionString"/>, which
/// points it at a host that cannot resolve — so this class is a host whose database is down, and
/// most of what step 54 splits apart can be asserted without a container at all. The one fact that
/// needs a real database builds its own factory over the shared one.
/// </para>
/// <para>
/// Shares <see cref="PostgresCollectionDefinition"/> — see its remarks — because it boots a real
/// host the same way <c>AuthFlowTests</c> does.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class HealthEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private readonly PostgresFixture _postgres;

    public HealthEndpointTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _postgres = postgres;
    }

    /// <summary>
    /// Liveness answers for the process and nothing else, so an unreachable database does not
    /// change its answer. This is the whole point of the split: a host in this state should be
    /// taken out of rotation, not restarted.
    /// </summary>
    [Theory]
    [InlineData("/health")]
    [InlineData("/health/live")]
    public async Task LivenessIsHealthyWithNoDatabase(string path)
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal("healthy", body?.Status);
    }

    [Fact]
    public async Task ReadinessIsUnhealthyAndNamesTheDatabaseWhenItCannotBeReached()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReadinessResponse>();

        Assert.Equal("unhealthy", body?.Status);
        Assert.Equal("unhealthy", body?.Checks[DatabaseHealthCheck.Name]);
    }

    [Fact]
    public async Task ReadinessIsHealthyAgainstARunningDatabase()
    {
        await using var connected = new ApiFactory { ConnectionString = _postgres.ConnectionString };
        using var client = connected.CreateClient();

        using var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ReadinessResponse>();

        Assert.Equal("healthy", body?.Status);
        Assert.Equal("healthy", body?.Checks[DatabaseHealthCheck.Name]);
    }

    /// <summary>
    /// The id a caller quotes in a support question is one it can choose, and every response
    /// carries it back — including, since it is the case that matters, one nothing went right in.
    /// </summary>
    [Fact]
    public async Task EveryResponseCarriesTheCallersOwnCorrelationId()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.Add("X-Correlation-ID", "dispatch-board-42");

        using var response = await client.SendAsync(request);

        Assert.Equal("dispatch-board-42", Assert.Single(response.Headers.GetValues("X-Correlation-ID")));
    }

    /// <summary>
    /// An id that could forge a log line or split a header is not repeated. The server answers
    /// with its own instead of sanitising the caller's into something it never sent.
    /// </summary>
    [Fact]
    public async Task ACorrelationIdThatCouldForgeALogLineIsRefused()
    {
        // A control character: the shape that forges a log entry, and the reason an inbound id is
        // read before it is repeated.
        const string Hostile = "forged\u0001entry";

        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health");
        request.Headers.TryAddWithoutValidation("X-Correlation-ID", Hostile);

        using var response = await client.SendAsync(request);

        var echoed = Assert.Single(response.Headers.GetValues("X-Correlation-ID"));

        Assert.NotEqual(Hostile, echoed);
        Assert.NotEmpty(echoed);
    }
}
