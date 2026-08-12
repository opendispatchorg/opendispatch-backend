using System.Net;
using System.Net.Http.Json;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts;
using OpenDispatch.Contracts.Board;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Contracts.Jobs;
using OpenDispatch.Contracts.Schedule;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Scheduling;

/// <summary>
/// Schedule and Dispatch over a real host and a real database (Document 3, step 48): a dispatcher
/// re-plans a day and watches the board reflect it, then slots in an emergency without disturbing
/// anyone else's morning — the two scenarios the step's own "Tests" line names.
/// </summary>
/// <remarks>
/// The engine's own behaviour — the search, reproducibility, what gets left unassigned and why — is
/// exhaustively covered in <c>Scheduling.Tests</c> and <c>Application.Tests</c>; none of that is
/// restated here. What only a real host adds is the wire: request DTOs reaching the right commands,
/// the board's <c>day</c> query resolving to the horizon the optimiser just wrote to, and both
/// surfaces sitting behind the same role check.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class ScheduleAndDispatchFlowTests : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset MorningOf = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly TheDay = DateOnly.FromDateTime(MorningOf.UtcDateTime);

    private readonly ApiFactory _factory;
    private readonly PostgresFixture _postgres;

    public ScheduleAndDispatchFlowTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _postgres = postgres;
        _factory.ConnectionString = postgres.ConnectionString;
    }

    [Fact]
    public async Task OptimizesThenTheBoardReflectsTheAssignment()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync(await SeedDispatcherAsync(org), "vance-refrigeration");

        var technicianId = await SeedTechnicianAsync(org);
        var job = await ABookedJobAsync(client, token);

        var optimized = await PostOkAsync<OptimizeScheduleResponse>(
            client,
            token,
            "/schedule/optimize",
            new OptimizeScheduleRequest(MorningOf, MorningOf.AddHours(9)));
        Assert.Equal(1, optimized.Planned);
        Assert.Empty(optimized.Unassigned);

        var board = await GetAsync<DispatchBoardResponse>(
            client, token, $"/dispatch/board?day={TheDay:yyyy-MM-dd}");

        var route = Assert.Single(board.Routes, route => route.TechnicianId == technicianId);
        var stop = Assert.Single(route.Stops);
        Assert.Equal(job.Id, stop.Job.JobId);
        Assert.Empty(board.Unassigned);

        var afterOptimize = await GetAsync<JobResponse>(client, token, $"/jobs/{job.Id}");
        Assert.Equal(JobStatus.Scheduled, afterOptimize.Status);
    }

    [Fact]
    public async Task InsertPlacesAnEmergencyJob()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync(await SeedDispatcherAsync(org), "vance-refrigeration");

        var technicianId = await SeedTechnicianAsync(org);
        var emergency = await ABookedJobAsync(client, token, JobPriority.Emergency);

        var inserted = await PostOkAsync<InsertJobResponse>(
            client, token, "/schedule/insert", new InsertJobRequest(emergency.Id));

        Assert.Equal(technicianId, inserted.TechnicianId);
        Assert.Equal(0, inserted.Sequence);
        Assert.Equal(0, inserted.Displaced);
        Assert.NotEqual(Guid.Empty, inserted.AssignmentId);

        var afterInsert = await GetAsync<JobResponse>(client, token, $"/jobs/{emergency.Id}");
        Assert.Equal(JobStatus.Scheduled, afterInsert.Status);
    }

    [Fact]
    public async Task ATechnicianCannotReachTheScheduleOrDispatchSurface()
    {
        using var client = _factory.CreateClient();
        await _factory.SeedUserAsync(OrgId.New(), "field@vance.example", "boiler-service-call", UserRole.Technician);
        var token = await client.LoginAsync("field@vance.example", "boiler-service-call");

        using var scheduleResponse = await SendAsync(
            client,
            token,
            HttpMethod.Post,
            "/schedule/optimize",
            new OptimizeScheduleRequest(MorningOf, MorningOf.AddHours(9)));
        Assert.Equal(HttpStatusCode.Forbidden, scheduleResponse.StatusCode);

        using var dispatchResponse = await SendAsync(
            client, token, HttpMethod.Get, $"/dispatch/board?day={TheDay:yyyy-MM-dd}", body: null);
        Assert.Equal(HttpStatusCode.Forbidden, dispatchResponse.StatusCode);
    }

    private async Task<string> SeedDispatcherAsync(OrgId org)
    {
        const string username = "dispatch@vance.example";
        await _factory.SeedUserAsync(org, username, "vance-refrigeration", UserRole.Dispatcher);

        return username;
    }

    /// <summary>
    /// Writes a technician straight through EF — <c>POST /technicians</c> is <c>AdminOnly</c>
    /// (step 47), and the dispatcher this flow runs as has no business calling it.
    /// </summary>
    private async Task<Guid> SeedTechnicianAsync(OrgId org)
    {
        var technician = TechnicianBuilder.Any().ForOrg(org).Named("Alex Rivera").Skilled("hvac").Build();

        await using var context = _postgres.NewContext(org);
        context.Technicians.Add(technician);
        await context.SaveChangesAsync();

        return technician.Id.Value;
    }

    private static async Task<JobResponse> ABookedJobAsync(
        HttpClient client, string token, JobPriority priority = JobPriority.Normal)
    {
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
                customer.Id,
                location.Id,
                "hvac",
                priority,
                MorningOf.AddHours(1),
                MorningOf.AddHours(4),
                TimeSpan.FromHours(1)));
    }

    private static async Task<TResponse> PostAsync<TResponse>(
        HttpClient client, string token, string path, object body)
    {
        using var response = await SendAsync(client, token, HttpMethod.Post, path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<TResponse>())!;
    }

    /// <summary>
    /// For <c>/schedule/optimize</c> and <c>/schedule/insert</c>: both plan work that already
    /// exists rather than create a resource at a new URI — the same 200-not-201 register as
    /// <c>POST /jobs/{id}/assign</c> (step 47).
    /// </summary>
    private static async Task<TResponse> PostOkAsync<TResponse>(
        HttpClient client, string token, string path, object body)
    {
        using var response = await SendAsync(client, token, HttpMethod.Post, path, body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

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
