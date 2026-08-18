using System.Net;
using System.Net.Http.Json;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Contracts.Jobs;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Jobs;

/// <summary>
/// The call-to-cash loop's demand side, over a real host and a real database (Document 3, step
/// 47): book a customer and a technician, book the job, plan it by hand, and walk it through its
/// early stages — the sequence a dispatcher actually performs, and the reason
/// <c>TESTING.md</c> asks for one flow like this rather than a test per endpoint.
/// </summary>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class JobEndpointsFlowTests : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset MorningOf = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly ApiFactory _factory;
    private readonly PostgresFixture _postgres;

    public JobEndpointsFlowTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _postgres = postgres;
        _factory.ConnectionString = postgres.ConnectionString;
    }

    [Fact]
    public async Task BooksAJobPlansItByHandAndWalksItThroughItsEarlyStages()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync(await SeedDispatcherAsync(org), "vance-refrigeration");

        var customer = await PostAsync<CustomerSummaryResponse>(
            client, token, "/customers", new CreateCustomerRequest("Vance Refrigeration", null, null));
        var location = await PostAsync<ServiceLocationResponse>(
            client,
            token,
            $"/customers/{customer.Id}/locations",
            new ServiceLocationRequest("Head office", "1 High Street, London", 51.5074d, -0.1278d));

        // Seeded straight through EF, not POST /technicians: that endpoint is AdminOnly (step
        // 47), and this dispatcher-run flow has no business asking for one.
        var technicianId = await SeedTechnicianAsync(org);

        var job = await PostAsync<JobResponse>(
            client,
            token,
            "/jobs",
            new CreateJobRequest(
                customer.Id,
                location.Id,
                "hvac",
                JobPriority.High,
                MorningOf.AddHours(1),
                MorningOf.AddHours(4),
                TimeSpan.FromHours(1)));
        Assert.Equal(JobStatus.Unscheduled, job.Status);

        // 200, not 201: this plans an existing job rather than creating a top-level resource at
        // a predictable URI — an action endpoint, in the same register as /status below it.
        using var assignResponse = await SendAsync(
            client, token, HttpMethod.Post, $"/jobs/{job.Id}/assign", new AssignJobRequest(technicianId, MorningOf.AddHours(1)));
        Assert.Equal(HttpStatusCode.OK, assignResponse.StatusCode);
        var assigned = await assignResponse.Content.ReadFromJsonAsync<AssignJobResponse>();
        Assert.NotNull(assigned);
        Assert.NotEqual(Guid.Empty, assigned.AssignmentId);

        var afterAssign = await GetAsync<JobResponse>(client, token, $"/jobs/{job.Id}");
        Assert.Equal(JobStatus.Scheduled, afterAssign.Status);

        using var dispatched = await SendAsync(
            client, token, HttpMethod.Post, $"/jobs/{job.Id}/status", new ChangeJobStatusRequest(JobStatus.Dispatched));
        Assert.Equal(HttpStatusCode.NoContent, dispatched.StatusCode);

        var afterDispatch = await GetAsync<JobResponse>(client, token, $"/jobs/{job.Id}");
        Assert.Equal(JobStatus.Dispatched, afterDispatch.Status);

        var page = await GetAsync<JobPageResponse>(client, token, "/jobs");
        var listed = page.Items;
        Assert.Contains(listed, listedJob => listedJob.Id == job.Id);
    }

    /// <summary>
    /// The conflict category through a real caller: a job already dispatched cannot be scheduled
    /// again by the same request. <c>ErrorMappingFlowTests</c> covers <c>NotFound</c> and the
    /// exception handler; this is the one this step's own slices produce.
    /// </summary>
    [Fact]
    public async Task AnIllegalStatusChangeIsAConflict()
    {
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync(await SeedDispatcherAsync(OrgId.New()), "vance-refrigeration");

        var customer = await PostAsync<CustomerSummaryResponse>(
            client, token, "/customers", new CreateCustomerRequest("Vance Refrigeration", null, null));
        var location = await PostAsync<ServiceLocationResponse>(
            client,
            token,
            $"/customers/{customer.Id}/locations",
            new ServiceLocationRequest("Head office", "1 High Street, London", 51.5074d, -0.1278d));
        var job = await PostAsync<JobResponse>(
            client,
            token,
            "/jobs",
            new CreateJobRequest(
                customer.Id, location.Id, "hvac", JobPriority.Normal,
                MorningOf.AddHours(1), MorningOf.AddHours(4), TimeSpan.FromHours(1)));

        // Unscheduled -> Cancelled is legal; Cancelled -> Scheduled is not (Job.AllowedTransitions).
        using var cancelled = await SendAsync(
            client, token, HttpMethod.Post, $"/jobs/{job.Id}/status", new ChangeJobStatusRequest(JobStatus.Cancelled));
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);

        using var response = await SendAsync(
            client, token, HttpMethod.Post, $"/jobs/{job.Id}/status", new ChangeJobStatusRequest(JobStatus.Scheduled));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task ATechnicianCannotReachTheJobsSurface()
    {
        using var client = _factory.CreateClient();
        await _factory.SeedUserAsync(OrgId.New(), "field@vance.example", "boiler-service-call", UserRole.Technician);
        var token = await client.LoginAsync("field@vance.example", "boiler-service-call");

        using var response = await SendAsync(client, token, HttpMethod.Get, "/jobs", body: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
