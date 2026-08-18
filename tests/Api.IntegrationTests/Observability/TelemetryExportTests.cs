using System.Net;
using System.Net.Http.Json;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Api.Observability;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Observability;

/// <summary>
/// What happens to a shop when its collector is not there.
/// </summary>
/// <remarks>
/// <para>
/// The one fact about the exporter worth a test, and it is not "does it export" — that is the
/// OpenTelemetry SDK's own business, and it is settled by pointing a real collector at a real host,
/// which was done by hand and written up rather than run on every build.
/// </para>
/// <para>
/// What this pins is the failure mode: a collector that is down, moved, or never existed must cost
/// the business nothing. Telemetry that can take the system with it is telemetry somebody
/// eventually rips out, and the shape of that mistake — an exporter awaited on the request path, a
/// startup that blocks on a connection — is easy to write and invisible until the collector has its
/// first outage.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class TelemetryExportTests : IClassFixture<ApiFactory>
{
    /// <summary>
    /// A collector that is not there. Port 4317 on a name that never resolves — RFC 2606's
    /// <c>.invalid</c>, the same trick <see cref="ApiFactory"/> uses for "no database requested".
    /// </summary>
    private const string AbsentCollector = "http://collector-that-is-not-running.invalid:4317";

    private readonly ApiFactory _factory;

    public TelemetryExportTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _factory.ConnectionString = postgres.ConnectionString;

        // Registration-time keys, so they go through UseSetting as well as the in-memory
        // collection — the arrangement the backplane already needed.
        _factory.Settings[Telemetry.EndpointKey] = AbsentCollector;
    }

    [Fact]
    public async Task TheShopKeepsWorkingWhenTheCollectorIsNot()
    {
        var org = OrgId.New();
        var username = $"otel-{Guid.NewGuid():N}@vance.example";
        await _factory.SeedUserAsync(org, username, "vance-refrigeration", UserRole.Dispatcher);

        using var client = _factory.CreateClient();
        var token = await client.LoginAsync(username, "vance-refrigeration");

        // A real write, with the exporter registered and its collector unreachable: the metrics it
        // records and the span it produces both go nowhere, and the customer is still taken on.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/customers").Authorized(token);
        request.Content = JsonContent.Create(new CreateCustomerRequest("Vance Refrigeration", null, null));

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // And it is not one request that survives: the export failure does not accumulate into
        // something that stops the next one either.
        using var readback = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "/customers").Authorized(token));

        Assert.Equal(HttpStatusCode.OK, readback.StatusCode);
    }
}
