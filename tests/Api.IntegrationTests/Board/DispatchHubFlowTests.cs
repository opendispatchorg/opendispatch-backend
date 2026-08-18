using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts;
using OpenDispatch.Contracts.Board;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Contracts.Jobs;
using OpenDispatch.Contracts.Schedule;
using OpenDispatch.Contracts.Technicians;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Board;

/// <summary>
/// The real-time board over a real host and a real database (Document 3, step 51): a job change
/// reaches the organization that owns it, live, and reaches nobody else.
/// </summary>
/// <remarks>
/// Long polling, not the default negotiated transport: <c>TestServer</c>'s in-memory handler has
/// no socket to upgrade, so a WebSocket connection has nothing to attach to. Long polling is
/// ordinary HTTP request/response, which <c>TestServer</c> already fully supports, and it is a
/// transport a browser genuinely falls back to — this is not a test-only shortcut standing in for
/// a mechanism the real server never uses.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class DispatchHubFlowTests : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan ReceiveTimeout = TimeSpan.FromSeconds(10);
    private static readonly DateTimeOffset MorningOf = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly ApiFactory _factory;

    public DispatchHubFlowTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _factory.ConnectionString = postgres.ConnectionString;
    }

    [Fact]
    public async Task AJobChangeReachesItsOwnOrgsGroupAndNoOthers()
    {
        var home = OrgId.New();
        var elsewhere = OrgId.New();

        using var httpClient = _factory.CreateClient();
        var homeToken = await httpClient.LoginAsync(await SeedDispatcherAsync(home, "dispatch@home.example"), "vance-refrigeration");
        var elsewhereToken = await httpClient.LoginAsync(
            await SeedDispatcherAsync(elsewhere, "dispatch@elsewhere.example"), "vance-refrigeration");

        await using var homeConnection = await ConnectedAsync(homeToken);
        await using var elsewhereConnection = await ConnectedAsync(elsewhereToken);

        var homeReceived = new List<JobUpdated>();
        var elsewhereReceived = new List<JobUpdated>();
        var homeGotOne = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        homeConnection.On<JobUpdated>(BoardEvents.JobUpdated, payload =>
        {
            homeReceived.Add(payload);
            homeGotOne.TrySetResult();
        });
        elsewhereConnection.On<JobUpdated>(BoardEvents.JobUpdated, payload => elsewhereReceived.Add(payload));

        var job = await ABookedJobAsync(httpClient, homeToken);

        // Unscheduled -> Cancelled: the simplest legal transition, and the one this flow does not
        // need a technician or a customer's second HTTP round trip to reach.
        using var cancelled = await SendAsync(
            httpClient, homeToken, HttpMethod.Post, $"/jobs/{job.Id}/status", new ChangeJobStatusRequest(JobStatus.Cancelled));
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);

        var won = await Task.WhenAny(homeGotOne.Task, Task.Delay(ReceiveTimeout));
        Assert.True(won == homeGotOne.Task, "the org that owns the job never heard about it changing.");

        var received = Assert.Single(homeReceived);
        Assert.Equal(job.Id, received.JobId);
        Assert.Equal(JobStatus.Cancelled, received.Status);

        // Give a message that was never coming a moment to arrive anyway, rather than trusting
        // that "nothing yet" at the instant above means "nothing ever" — the same round trip that
        // delivered the home group's push is what elsewhere's absence is judged against.
        await Task.Delay(TimeSpan.FromMilliseconds(250));
        Assert.Empty(elsewhereReceived);
    }

    /// <summary>
    /// The case that had never worked: optimising a day repaints the boards watching it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A newly planned stop is created, never rescheduled, and <c>Assignment.Create</c> raised
    /// nothing — so the flagship feature, the live board, went dark on exactly the act it exists to
    /// show. The dispatcher who pressed Optimise saw the new plan on their own next read; everybody
    /// else's screen kept yesterday's until they refreshed.
    /// </para>
    /// <para>
    /// Asserted through <c>assignment.updated</c> rather than <c>job.updated</c> deliberately. The
    /// job transition to <c>Scheduled</c> has always pushed, which is what made this look like it
    /// worked — a board that redraws a job's status chip while its stop is missing from the lane is
    /// the failure, not the absence of any message at all.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task OptimisingADayPutsItsNewStopsOnTheBoard()
    {
        var org = OrgId.New();

        using var httpClient = _factory.CreateClient();
        var token = await httpClient.LoginAsync(
            await SeedAdminAsync(org, $"admin-{Guid.NewGuid():N}@vance.example"), "vance-refrigeration");

        await using var connection = await ConnectedAsync(token);

        var received = new List<AssignmentUpdated>();
        var arrived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<AssignmentUpdated>(BoardEvents.AssignmentUpdated, payload =>
        {
            received.Add(payload);
            arrived.TrySetResult();
        });

        var job = await ABookedJobAsync(httpClient, token);

        using var technicianResponse = await SendAsync(
            httpClient,
            token,
            HttpMethod.Post,
            "/technicians",
            new CreateTechnicianRequest(
                "Sam Rivera",
                ["hvac"],
                MorningOf,
                MorningOf.AddHours(9),
                51.5074d,
                -0.1278d));
        Assert.Equal(HttpStatusCode.Created, technicianResponse.StatusCode);

        using var optimized = await SendAsync(
            httpClient,
            token,
            HttpMethod.Post,
            "/schedule/optimize",
            new OptimizeScheduleRequest(MorningOf, MorningOf.AddHours(9)));
        Assert.Equal(HttpStatusCode.OK, optimized.StatusCode);

        var won = await Task.WhenAny(arrived.Task, Task.Delay(ReceiveTimeout));
        Assert.True(won == arrived.Task, "optimising the day put a stop on nobody's board.");

        var stop = Assert.Single(received);
        Assert.Equal(job.Id, stop.JobId);
        Assert.Equal(0, stop.Sequence);
    }

    private async Task<HubConnection> ConnectedAsync(string token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "/hubs/dispatch"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();

        await connection.StartAsync();

        return connection;
    }

    private async Task<string> SeedDispatcherAsync(OrgId org, string username)
    {
        await _factory.SeedUserAsync(org, username, "vance-refrigeration", UserRole.Dispatcher);

        return username;
    }

    /// <summary>
    /// An admin rather than a dispatcher, because creating a technician is an admin's act — and the
    /// optimise this test is about is reachable by both.
    /// </summary>
    private async Task<string> SeedAdminAsync(OrgId org, string username)
    {
        await _factory.SeedUserAsync(org, username, "vance-refrigeration", UserRole.Admin);

        return username;
    }

    private static async Task<JobResponse> ABookedJobAsync(HttpClient client, string token)
    {
        var morningOf = MorningOf;

        var customer = await PostAsync<CustomerSummaryResponse>(
            client, token, "/customers", new CreateCustomerRequest("Vance Refrigeration", null, null));
        var location = await PostAsync<ServiceLocationResponse>(
            client,
            token,
            $"/customers/{customer.Id}/locations",
            new ServiceLocationRequest("Head office", "1 High Street, London", 51.5074d, -0.1278d));

        return await PostAsync<JobResponse>(
            client,
            token,
            "/jobs",
            new CreateJobRequest(
                customer.Id, location.Id, "hvac", JobPriority.Normal,
                morningOf.AddHours(1), morningOf.AddHours(4), TimeSpan.FromHours(1)));
    }

    private static async Task<TResponse> PostAsync<TResponse>(
        HttpClient client, string token, string path, object body)
    {
        using var response = await SendAsync(client, token, HttpMethod.Post, path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

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
