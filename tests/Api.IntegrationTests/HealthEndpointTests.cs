using System.Net;
using System.Net.Http.Json;
using OpenDispatch.Api.Health;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests;

/// <summary>
/// Boots the real host through <see cref="ApiFactory"/>, so this exercises configuration
/// binding, startup validation and routing end to end rather than calling the endpoint
/// delegate directly.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class HealthEndpointTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public HealthEndpointTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task HealthReturnsOk()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal("healthy", body?.Status);
    }
}
