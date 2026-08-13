using System.Net;
using System.Net.Http.Json;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Customers;

/// <summary>
/// Customers and their locations, over a real host and a real database (Document 3, step 47).
/// </summary>
/// <remarks>
/// One flow covers the CRUD, as <c>TESTING.md</c> asks: take on a customer, give them a
/// location, correct both, read them back, then take the location off again. What is tested one
/// case at a time is what is not CRUD — the role a caller needs, and a miss.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class CustomerEndpointsFlowTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public CustomerEndpointsFlowTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _factory.ConnectionString = postgres.ConnectionString;
    }

    [Fact]
    public async Task TakesOnACustomerCorrectsThemAndManagesTheirLocations()
    {
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync(
            await SeedDispatcherAsync(), "vance-refrigeration");

        var created = await PostAsync<CustomerSummaryResponse>(
            client, token, "/customers", new CreateCustomerRequest("Vance Refrigeration", "hello@vance.example", null));
        Assert.Equal("Vance Refrigeration", created.Name);

        var located = await PostAsync<ServiceLocationResponse>(
            client,
            token,
            $"/customers/{created.Id}/locations",
            new ServiceLocationRequest("Head office", "1 High Street, London", 51.5074d, -0.1278d));

        using var renamed = await SendAsync(
            client, token, HttpMethod.Put, $"/customers/{created.Id}",
            new UpdateCustomerRequest("Vance Refrigeration Ltd", "office@vance.example", "+44 20 7946 0000"));
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);

        using var relocated = await SendAsync(
            client, token, HttpMethod.Put, $"/customers/{created.Id}/locations/{located.Id}",
            new ServiceLocationRequest("New head office", "9 New Street, London", 51.51d, -0.12d));
        Assert.Equal(HttpStatusCode.NoContent, relocated.StatusCode);

        var fetched = await GetAsync<CustomerResponse>(client, token, $"/customers/{created.Id}");
        Assert.Equal("Vance Refrigeration Ltd", fetched.Name);
        Assert.Equal("office@vance.example", fetched.Email);
        Assert.Equal("+44 20 7946 0000", fetched.Phone);
        var onlyLocation = Assert.Single(fetched.Locations);
        Assert.Equal("New head office", onlyLocation.Label);
        Assert.Equal("9 New Street, London", onlyLocation.Address);

        var listed = await GetAsync<CustomerSummaryResponse[]>(client, token, "/customers");
        Assert.Contains(listed, customer => customer.Id == created.Id);

        using var removed = await SendAsync(
            client, token, HttpMethod.Delete, $"/customers/{created.Id}/locations/{located.Id}", body: null);
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);

        var afterRemoval = await GetAsync<CustomerResponse>(client, token, $"/customers/{created.Id}");
        Assert.Empty(afterRemoval.Locations);
    }

    [Fact]
    public async Task GettingACustomerThisTenantDoesNotHaveIsA404()
    {
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync(await SeedDispatcherAsync(), "vance-refrigeration");

        using var response = await SendAsync(client, token, HttpMethod.Get, $"/customers/{Guid.NewGuid()}", body: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ATechnicianCannotReachTheCustomersSurface()
    {
        using var client = _factory.CreateClient();
        await _factory.SeedUserAsync(OrgId.New(), "field@vance.example", "boiler-service-call", UserRole.Technician);
        var token = await client.LoginAsync("field@vance.example", "boiler-service-call");

        using var response = await SendAsync(client, token, HttpMethod.Get, "/customers", body: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<string> SeedDispatcherAsync()
    {
        const string username = "dispatch@vance.example";
        await _factory.SeedUserAsync(OrgId.New(), username, "vance-refrigeration", UserRole.Dispatcher);

        return username;
    }

    private static async Task<TResponse> PostAsync<TResponse>(
        HttpClient client, string token, string path, object body)
    {
        using var response = await SendAsync(client, token, HttpMethod.Post, path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<TResponse>())!;
    }

    private static async Task<TResponse> GetAsync<TResponse>(HttpClient client, string token, string path)
    {
        using var response = await SendAsync(client, token, HttpMethod.Get, path, body: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<TResponse>())!;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, string token, HttpMethod method, string path, object? body)
    {
        var request = new HttpRequestMessage(method, path).Authorized(token);

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return client.SendAsync(request);
    }
}
