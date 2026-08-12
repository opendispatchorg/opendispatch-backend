using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts.Auth;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Auth;

/// <summary>
/// Login and role enforcement against the real host — the first requests in this suite that go
/// through <c>UseAuthentication</c>/<c>UseAuthorization</c>, which is why they have to be here
/// rather than sent through <c>ISender</c> directly like the pre-controller slices are: both are
/// ASP.NET Core middleware behaviour, invisible to anything that does not make a real HTTP
/// request against a real host.
/// </summary>
/// <remarks>
/// The minimal user store is an in-memory singleton (<c>InMemoryUserStore</c>), so nothing here
/// touches a database — but it shares <see cref="PostgresCollectionDefinition"/> with
/// <c>HealthEndpointTests</c> anyway, because both boot a real host; see that collection's
/// remarks for why.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class AuthFlowTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public AuthFlowTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task LoginWithTheRightPasswordReturnsAToken()
    {
        await _factory.SeedUserAsync(OrgId.New(), "dana@vance.example", "correct horse battery", UserRole.Admin);
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync(
            "/auth/login",
            new LoginRequest("dana@vance.example", "correct horse battery"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();

        Assert.False(string.IsNullOrWhiteSpace(body?.Token));
        Assert.True(body!.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData("dana@vance.example", "the wrong password")]
    [InlineData("nobody-by-this-name@vance.example", "correct horse battery")]
    public async Task LoginWithWrongCredentialsIsRejected(string username, string password)
    {
        await _factory.SeedUserAsync(OrgId.New(), "dana@vance.example", "correct horse battery", UserRole.Admin);
        using var client = _factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(username, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AProtectedEndpointRejectsAnAnonymousCaller()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/auth/_test/admin-only");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AProtectedEndpointRejectsTheWrongRole()
    {
        await _factory.SeedUserAsync(OrgId.New(), "priya@vance.example", "shift-plan-monday", UserRole.Dispatcher);
        using var client = _factory.CreateClient();
        var token = await LoginAsync(client, "priya@vance.example", "shift-plan-monday");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/_test/admin-only");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AProtectedEndpointAcceptsTheRoleItAsksFor()
    {
        await _factory.SeedUserAsync(OrgId.New(), "sam@vance.example", "boiler-service-call", UserRole.Admin);
        using var client = _factory.CreateClient();
        var token = await LoginAsync(client, "sam@vance.example", "boiler-service-call");

        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/_test/admin-only");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private static async Task<string> LoginAsync(HttpClient client, string username, string password)
    {
        using var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(username, password));
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();

        return body!.Token;
    }
}
