using System.Net;
using System.Net.Http.Json;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts.Technicians;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Technicians;

/// <summary>
/// The crew, over a real host and a real database (Document 3, step 47).
/// </summary>
/// <remarks>
/// One flow covers the CRUD plus skills and shift. Role enforcement is checked one case at a
/// time: reading the crew is <c>AdminOrDispatcher</c>, changing it is <c>AdminOnly</c> — see
/// <c>TechnicianEndpoints</c>' own remarks for why the two differ.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class TechnicianEndpointsFlowTests : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset MorningOf = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly ApiFactory _factory;

    public TechnicianEndpointsFlowTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _factory.ConnectionString = postgres.ConnectionString;
    }

    [Fact]
    public async Task TakesOnATechnicianCorrectsThemAndSetsSkillsAndShift()
    {
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync(await SeedAdminAsync(), "riverside-heating");

        var created = await PostAsync<TechnicianResponse>(
            client,
            token,
            "/technicians",
            new CreateTechnicianRequest(
                "Alex Rivera", ["hvac"], MorningOf, MorningOf.AddHours(9), 51.5074d, -0.1278d));
        Assert.Equal("Alex Rivera", created.Name);
        Assert.Equal(["hvac"], created.Skills);

        using var renamed = await SendAsync(
            client, token, HttpMethod.Put, $"/technicians/{created.Id}",
            new UpdateTechnicianRequest("Alexis Rivera", 51.51d, -0.12d));
        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);

        using var reskilled = await SendAsync(
            client, token, HttpMethod.Put, $"/technicians/{created.Id}/skills",
            new SetSkillsRequest(["hvac", "electrical"]));
        Assert.Equal(HttpStatusCode.NoContent, reskilled.StatusCode);

        using var reshifted = await SendAsync(
            client, token, HttpMethod.Put, $"/technicians/{created.Id}/shift",
            new SetShiftRequest(MorningOf.AddHours(1), MorningOf.AddHours(10)));
        Assert.Equal(HttpStatusCode.NoContent, reshifted.StatusCode);

        var fetched = await GetAsync<TechnicianResponse>(client, token, $"/technicians/{created.Id}");
        Assert.Equal("Alexis Rivera", fetched.Name);
        Assert.Equal(["electrical", "hvac"], fetched.Skills.Order(StringComparer.Ordinal));
        Assert.Equal(MorningOf.AddHours(1), fetched.ShiftStart);
        Assert.Equal(MorningOf.AddHours(10), fetched.ShiftEnd);

        var listed = await GetAsync<TechnicianResponse[]>(client, token, "/technicians");
        Assert.Contains(listed, technician => technician.Id == created.Id);
    }

    [Fact]
    public async Task GettingATechnicianThisTenantDoesNotHaveIsA404()
    {
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync(await SeedAdminAsync(), "riverside-heating");

        using var response = await SendAsync(client, token, HttpMethod.Get, $"/technicians/{Guid.NewGuid()}", body: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ADispatcherMayReadTheCrewButNotChangeIt()
    {
        using var client = _factory.CreateClient();
        await _factory.SeedUserAsync(OrgId.New(), "dispatch@riverside.example", "riverside-heating", UserRole.Dispatcher);
        var token = await client.LoginAsync("dispatch@riverside.example", "riverside-heating");

        using var read = await SendAsync(client, token, HttpMethod.Get, "/technicians", body: null);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);

        using var write = await SendAsync(
            client, token, HttpMethod.Post, "/technicians",
            new CreateTechnicianRequest("Alex Rivera", ["hvac"], MorningOf, MorningOf.AddHours(9), 51.5074d, -0.1278d));
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
    }

    private async Task<string> SeedAdminAsync()
    {
        const string username = "admin@riverside.example";
        await _factory.SeedUserAsync(OrgId.New(), username, "riverside-heating", UserRole.Admin);

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
