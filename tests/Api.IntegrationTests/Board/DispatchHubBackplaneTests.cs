using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using OpenDispatch.Api.Board;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts;
using OpenDispatch.Contracts.Board;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Contracts.Jobs;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;
using Testcontainers.Redis;

namespace OpenDispatch.Api.IntegrationTests.Board;

/// <summary>
/// The board across two hosts, which is the only arrangement that can tell whether the backplane
/// does anything.
/// </summary>
/// <remarks>
/// <para>
/// SignalR keeps its groups in the memory of the process holding the connection, so a board
/// connected to one instance never hears about a change made through another — silently, with
/// nothing logged and nothing failed. One host cannot demonstrate that, which is exactly why it
/// went unnoticed: every test in this suite until now has been a single host talking to itself.
/// </para>
/// <para>
/// So this starts two whole hosts over one Redis and one database, connects a dispatcher to the
/// first, and changes a job through the second. Without <c>SignalR:Redis</c> the wait times out;
/// with it the push arrives. That is the entire fix, stated as a test.
/// </para>
/// <para>
/// Its own Redis container rather than the shared Postgres fixture's: this is the only test that
/// needs one, and a container the rest of the suite pays to start on every run would be a tax on
/// everybody for one fact.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class DispatchHubBackplaneTests : IAsyncLifetime
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(10);

    // The image is named rather than defaulted, like the PostGIS one the shared fixture pins: what
    // the suite runs against should not change under it when a package updates.
    private readonly RedisContainer _redis = new RedisBuilder("redis:7-alpine").Build();
    private readonly PostgresFixture _postgres;

    public DispatchHubBackplaneTests(PostgresFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => _redis.StartAsync();

    public Task DisposeAsync() => _redis.DisposeAsync().AsTask();

    [Fact]
    public async Task AChangeMadeOnOneHostReachesABoardConnectedToAnother()
    {
        var org = OrgId.New();

        await using var listening = HostAsync();
        await using var acting = HostAsync();

        using var listeningClient = listening.CreateClient();
        using var actingClient = acting.CreateClient();

        var username = $"dispatch-{Guid.NewGuid():N}@vance.example";
        await listening.SeedUserAsync(org, username, "vance-refrigeration", UserRole.Dispatcher);

        var boardToken = await listeningClient.LoginAsync(username, "vance-refrigeration");
        var workingToken = await actingClient.LoginAsync(username, "vance-refrigeration");

        await using var board = await ConnectedAsync(listening, boardToken);

        var received = new List<JobUpdated>();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        board.On<JobUpdated>(BoardEvents.JobUpdated, payload =>
        {
            received.Add(payload);
            arrived.TrySetResult();
        });

        // Everything from here happens on the *other* host.
        var job = await ABookedJobAsync(actingClient, workingToken);

        using var cancelled = await actingClient.SendAsync(
            new HttpRequestMessage(HttpMethod.Post, $"/jobs/{job.Id}/status")
            {
                Content = JsonContent.Create(new ChangeJobStatusRequest(JobStatus.Cancelled)),
            }.Authorized(workingToken));

        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);

        var won = await Task.WhenAny(arrived.Task, Task.Delay(ReceiveTimeout));

        Assert.True(
            won == arrived.Task,
            "a board connected to one instance never heard about a change made through another — "
                + "which is what the deployment looks like the moment there are two of them.");

        var update = Assert.Single(received);
        Assert.Equal(job.Id, update.JobId);
        Assert.Equal(JobStatus.Cancelled, update.Status);
    }

    /// <summary>One of the two hosts: same database, same Redis, separate process-in-process.</summary>
    private ApiFactory HostAsync() => new()
    {
        ConnectionString = _postgres.ConnectionString,
        Settings = { [BoardBackplane.ConnectionKey] = _redis.GetConnectionString() },
    };

    private static async Task<HubConnection> ConnectedAsync(ApiFactory host, string token)
    {
        // Long polling for the reason DispatchHubFlowTests gives: TestServer has no socket to
        // upgrade, and long polling is ordinary HTTP that a browser genuinely falls back to.
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(host.Server.BaseAddress, "/hubs/dispatch"), options =>
            {
                options.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        await connection.StartAsync();

        return connection;
    }

    private static async Task<JobResponse> ABookedJobAsync(HttpClient client, string token)
    {
        var customer = await PostAsync<CustomerSummaryResponse>(
            client, token, "/customers", new CreateCustomerRequest("Vance Refrigeration", null, null));

        var location = await PostAsync<ServiceLocationResponse>(
            client,
            token,
            $"/customers/{customer.Id}/locations",
            new ServiceLocationRequest("Head office", "12 Bath Road, Slough", 51.5107, -0.5950));

        var morning = new DateTimeOffset(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

        return await PostAsync<JobResponse>(
            client,
            token,
            "/jobs",
            new CreateJobRequest(
                customer.Id,
                location.Id,
                "hvac",
                JobPriority.Normal,
                morning,
                morning.AddHours(4),
                TimeSpan.FromHours(1)));
    }

    private static async Task<TResponse> PostAsync<TResponse>(
        HttpClient client, string token, string url, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body),
        }.Authorized(token);

        using var response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        return (await response.Content.ReadFromJsonAsync<TResponse>())!;
    }
}
