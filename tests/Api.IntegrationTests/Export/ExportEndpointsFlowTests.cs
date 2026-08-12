using System.Net;
using System.Net.Http.Json;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Contracts.Export;
using OpenDispatch.Contracts.Invoicing;
using OpenDispatch.Contracts.Jobs;
using OpenDispatch.Contracts.Technicians;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Export;

/// <summary>
/// <c>GET /export</c> over a real host and a real database (Document 3, step 49): Document 1's
/// anti-lock-in promise, proven against another tenant's rows actually sitting in the same tables.
/// </summary>
/// <remarks>
/// The build text asks for two things: that the snapshot is coherent, and that it is
/// tenant-scoped. Coherent is checked by walking a whole call-to-cash loop through the real HTTP
/// surface first — a customer, a job, a stop, a bill — so the export is read back against data this
/// same test built rather than fixtures nobody trusts. Tenant-scoped is checked by seeding a second
/// organization's customer straight through EF (the same shortcut every tenancy test since step 45
/// takes) and asserting it never appears.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class ExportEndpointsFlowTests : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset MorningOf = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly ApiFactory _factory;
    private readonly PostgresFixture _postgres;

    public ExportEndpointsFlowTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _postgres = postgres;
        _factory.ConnectionString = postgres.ConnectionString;
    }

    [Fact]
    public async Task ExportReturnsACoherentTenantScopedSnapshot()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync(await SeedAdminAsync(org), "riverside-heating");

        var technician = await PostAsync<TechnicianResponse>(
            client,
            token,
            "/technicians",
            new CreateTechnicianRequest(
                "Alex Rivera", ["hvac"], MorningOf, MorningOf.AddHours(9), 51.5074d, -0.1278d));

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

        using var assigned = await SendAsync(
            client, token, HttpMethod.Post, $"/jobs/{job.Id}/assign",
            new AssignJobRequest(technician.Id, MorningOf.AddHours(1)));
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var assignment = await assigned.Content.ReadFromJsonAsync<AssignJobResponse>();

        foreach (var status in new[] { JobStatus.Dispatched, JobStatus.EnRoute, JobStatus.InProgress, JobStatus.Completed })
        {
            using var response = await SendAsync(
                client, token, HttpMethod.Post, $"/jobs/{job.Id}/status", new ChangeJobStatusRequest(status));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        var invoice = await PostAsync<InvoiceResponse>(
            client,
            token,
            $"/jobs/{job.Id}/invoice",
            new CreateInvoiceRequest([new InvoiceLineRequest(LineItemKind.Labor, "Callout", 1m, 90m)]));

        // Another organization, with a customer of its own, written straight through EF — the same
        // shortcut TenantResolutionFlowTests took at step 45, because this test is about whether a
        // read is scoped, not about how a second tenant's row gets written.
        await SeedForeignCustomerAsync();

        var export = await GetAsync<ExportResponse>(client, token, "/export");

        var onlyCustomer = Assert.Single(export.Customers);
        Assert.Equal(customer.Id, onlyCustomer.Id);
        Assert.Equal("Vance Refrigeration", onlyCustomer.Name);
        var onlyLocation = Assert.Single(onlyCustomer.Locations);
        Assert.Equal(location.Id, onlyLocation.Id);

        var onlyJob = Assert.Single(export.Jobs);
        Assert.Equal(job.Id, onlyJob.Id);
        Assert.Equal(JobStatus.Invoiced, onlyJob.Status);

        var onlyAssignment = Assert.Single(export.Assignments);
        Assert.Equal(assignment!.AssignmentId, onlyAssignment.Id);
        Assert.Equal(job.Id, onlyAssignment.JobId);
        Assert.Equal(technician.Id, onlyAssignment.TechnicianId);

        var onlyInvoice = Assert.Single(export.Invoices);
        Assert.Equal(invoice.Id, onlyInvoice.Id);
        Assert.Equal(job.Id, onlyInvoice.JobId);
        Assert.Equal(90m, onlyInvoice.Total);
        Assert.Equal(InvoiceStatus.Draft, onlyInvoice.Status);
    }

    [Fact]
    public async Task ADispatcherCannotReachTheExportSurface()
    {
        using var client = _factory.CreateClient();
        await _factory.SeedUserAsync(OrgId.New(), "dispatch@vance.example", "vance-refrigeration", UserRole.Dispatcher);
        var token = await client.LoginAsync("dispatch@vance.example", "vance-refrigeration");

        using var response = await SendAsync(client, token, HttpMethod.Get, "/export", body: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task SeedForeignCustomerAsync()
    {
        var foreignOrg = OrgId.New();
        var foreignCustomer = CustomerBuilder.Any().ForOrg(foreignOrg).Named("Ivy Fabrication").Build();

        await using var context = _postgres.NewContext(foreignOrg);
        context.Customers.Add(foreignCustomer);
        await context.SaveChangesAsync();
    }

    private async Task<string> SeedAdminAsync(OrgId org)
    {
        const string username = "admin@riverside.example";
        await _factory.SeedUserAsync(org, username, "riverside-heating", UserRole.Admin);

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
