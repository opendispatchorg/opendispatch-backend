using System.Net;
using System.Net.Http.Json;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Contracts.Invoicing;
using OpenDispatch.Contracts.Jobs;
using OpenDispatch.Contracts.Technicians;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Invoicing;

/// <summary>
/// The end of the call-to-cash loop, over a real host and a real database (Document 3, step 49):
/// a job is booked, worked, billed and paid, in the order the build text's own "Tests" line names.
/// </summary>
/// <remarks>
/// The money arithmetic and the state machine's own rules are exhaustively covered where they
/// belong — <c>InvoicingTests</c> and <c>InvoicingFlowTests</c>, both against the MediatR pipeline
/// directly. What only a real host adds is the wire: the two new DTOs, the 201/204 register, and
/// the one role boundary this step introduces — <c>AdminOnly</c>, not <c>AdminOrDispatcher</c>.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class InvoiceEndpointsFlowTests : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset MorningOf = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly ApiFactory _factory;

    public InvoiceEndpointsFlowTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _factory.ConnectionString = postgres.ConnectionString;
    }

    [Fact]
    public async Task InvoicesACompletedJobThenSettlesIt()
    {
        using var client = _factory.CreateClient();
        var token = await client.LoginAsync(await SeedAdminAsync(), "riverside-heating");

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

        foreach (var status in new[] { JobStatus.Dispatched, JobStatus.EnRoute, JobStatus.InProgress, JobStatus.Completed })
        {
            using var response = await SendAsync(
                client, token, HttpMethod.Post, $"/jobs/{job.Id}/status", new ChangeJobStatusRequest(status));
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        }

        // 201, not 200: raising an invoice creates a new aggregate at a predictable URI, the same
        // register as every other genuine creation in this API — unlike /jobs/{id}/assign beside
        // it, which plans an existing job rather than minting a new resource.
        var invoice = await PostAsync<InvoiceResponse>(
            client,
            token,
            $"/jobs/{job.Id}/invoice",
            new CreateInvoiceRequest(
            [
                new InvoiceLineRequest(LineItemKind.Labor, "Diagnosis and repair", 2.5m, 65m),
                new InvoiceLineRequest(LineItemKind.Part, "Expansion vessel", 1m, 84.99m),
            ]));
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal(247.49m, invoice.Total);

        var afterInvoice = await GetAsync<JobResponse>(client, token, $"/jobs/{job.Id}");
        Assert.Equal(JobStatus.Invoiced, afterInvoice.Status);

        // 204, matching /jobs/{id}/status beside it: this settles an existing bill rather than
        // creating anything.
        using var paid = await SendAsync(client, token, HttpMethod.Post, $"/invoices/{invoice.Id}/pay", body: null);
        Assert.Equal(HttpStatusCode.NoContent, paid.StatusCode);

        var afterPay = await GetAsync<JobResponse>(client, token, $"/jobs/{job.Id}");
        Assert.Equal(JobStatus.Paid, afterPay.Status);
    }

    [Fact]
    public async Task ADispatcherCannotReachTheInvoicingSurface()
    {
        using var client = _factory.CreateClient();
        await _factory.SeedUserAsync(OrgId.New(), "dispatch@vance.example", "vance-refrigeration", UserRole.Dispatcher);
        var token = await client.LoginAsync("dispatch@vance.example", "vance-refrigeration");

        using var response = await SendAsync(
            client,
            token,
            HttpMethod.Post,
            $"/jobs/{Guid.NewGuid()}/invoice",
            new CreateInvoiceRequest([new InvoiceLineRequest(LineItemKind.Labor, "Callout", 1m, 90m)]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
