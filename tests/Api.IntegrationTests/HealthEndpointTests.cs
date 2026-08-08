using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using OpenDispatch.Api.Health;

namespace OpenDispatch.Api.IntegrationTests;

/// <summary>
/// Boots the real host through <see cref="WebApplicationFactory{TEntryPoint}"/>, so this
/// exercises configuration binding, startup validation and routing end to end rather than
/// calling the endpoint delegate directly.
/// </summary>
public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

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
