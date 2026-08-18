using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Observability;
using OpenDispatch.Contracts;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Contracts.Jobs;
using OpenDispatch.Contracts.Sync;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Api.IntegrationTests.Sync;

/// <summary>
/// The offline sync protocol over a real host and a real database (Document 3, step 50): a
/// technician's phone empties its queue and asks what it missed — the three scenarios the build
/// text's own "Tests" line names.
/// </summary>
/// <remarks>
/// The deep mechanics — windowing, scope, idempotency within and across pushes, the whole conflict
/// policy — are exhaustively covered against the MediatR pipeline directly in
/// <c>PushOpsFlowTests</c>/<c>PullChangesFlowTests</c> and the unit tests beside them; none of that
/// is restated here. What only a real host adds is the wire: the two new DTOs, the technician
/// resolved from the JWT rather than passed as a parameter, and the code → reason mapping this step
/// writes.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class SyncEndpointsFlowTests : IClassFixture<ApiFactory>
{
    private static readonly DateTimeOffset MorningOf = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly ApiFactory _factory;
    private readonly PostgresFixture _postgres;

    public SyncEndpointsFlowTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _postgres = postgres;
        _factory.ConnectionString = postgres.ConnectionString;
    }

    [Fact]
    public async Task PushesFieldOpsThenAPullReflectsThem()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();
        var adminToken = await client.LoginAsync(await SeedAdminAsync(org), "riverside-heating");

        var technicianId = await SeedTechnicianAsync(org);
        var techToken = await client.LoginAsync(
            await SeedTechnicianUserAsync(org, technicianId), "boiler-service-call");

        var job = await ABookedAndAssignedJobAsync(client, adminToken, technicianId.Value);

        var opId = Guid.NewGuid();
        using var pushed = await SendAsync(
            client,
            techToken,
            HttpMethod.Post,
            "/sync/push",
            new SyncPushRequest(
            [
                new SyncOp(
                    opId,
                    "job",
                    job.Id,
                    "status_change",
                    JsonSerializer.SerializeToElement(new { status = "EnRoute" }),
                    BaseVersion: 0,
                    ClientTs: MorningOf.AddHours(1)),
            ]));
        Assert.Equal(HttpStatusCode.OK, pushed.StatusCode);
        var pushedBody = await pushed.Content.ReadFromJsonAsync<SyncPushResponse>();
        Assert.Equal(opId, Assert.Single(pushedBody!.Applied));
        Assert.Empty(pushedBody.Conflicts);

        using var pulled = await SendAsync(client, techToken, HttpMethod.Get, "/sync/pull?since=0", body: null);
        Assert.Equal(HttpStatusCode.OK, pulled.StatusCode);
        var pulledBody = await pulled.Content.ReadFromJsonAsync<SyncPullResponse>();

        var jobChange = Assert.Single(
            pulledBody!.Changes, change => change.Entity == "job" && change.EntityId == job.Id);
        Assert.False(jobChange.Deleted);
        var jobState = jobChange.State!.Value.Deserialize<SyncJobPayload>()!;
        Assert.Equal(JobStatus.EnRoute, jobState.Status);

        var stopChange = Assert.Single(pulledBody.Changes, change => change.Entity == "assignment");
        var stopState = stopChange.State!.Value.Deserialize<SyncStopPayload>()!;
        Assert.Equal(job.Id, stopState.JobId);
    }

    [Fact]
    public async Task AReSentPushIsIdempotent()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();
        var adminToken = await client.LoginAsync(await SeedAdminAsync(org), "riverside-heating");
        var technicianId = await SeedTechnicianAsync(org);
        var techToken = await client.LoginAsync(
            await SeedTechnicianUserAsync(org, technicianId), "boiler-service-call");
        var job = await ABookedAndAssignedJobAsync(client, adminToken, technicianId.Value);

        var opId = Guid.NewGuid();
        var request = new SyncPushRequest(
        [
            new SyncOp(
                opId,
                "job",
                job.Id,
                "add_line_item",
                JsonSerializer.SerializeToElement(
                    new { kind = "Part", description = "Run capacitor", quantity = 2, unitPriceCents = 2_850 }),
                BaseVersion: 0,
                ClientTs: MorningOf.AddHours(1)),
        ]);

        using var first = await SendAsync(client, techToken, HttpMethod.Post, "/sync/push", request);
        var firstBody = await first.Content.ReadFromJsonAsync<SyncPushResponse>();
        Assert.Equal(opId, Assert.Single(firstBody!.Applied));

        // Re-sent — the same request, the device's idempotency key unchanged.
        using var second = await SendAsync(client, techToken, HttpMethod.Post, "/sync/push", request);
        var secondBody = await second.Content.ReadFromJsonAsync<SyncPushResponse>();
        Assert.Equal(opId, Assert.Single(secondBody!.Applied));
        Assert.Empty(secondBody.Conflicts);

        // Applied once, not twice: one recorded line, not two.
        await using var context = _postgres.NewContext(org);
        var stored = await context.Jobs.SingleAsync(candidate => candidate.Id == JobId.From(job.Id));
        Assert.Single(stored.Lines);
    }

    [Fact]
    public async Task AnIllegalTransitionIsSurfacedAsAConflict()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();
        var adminToken = await client.LoginAsync(await SeedAdminAsync(org), "riverside-heating");
        var technicianId = await SeedTechnicianAsync(org);
        var techToken = await client.LoginAsync(
            await SeedTechnicianUserAsync(org, technicianId), "boiler-service-call");
        var job = await ABookedAndAssignedJobAsync(client, adminToken, technicianId.Value);

        // Driven all the way to Completed, legally, one op at a time.
        foreach (var status in new[] { "EnRoute", "InProgress", "Completed" })
        {
            using var response = await SendAsync(
                client,
                techToken,
                HttpMethod.Post,
                "/sync/push",
                new SyncPushRequest(
                [
                    new SyncOp(
                        Guid.NewGuid(),
                        "job",
                        job.Id,
                        "status_change",
                        JsonSerializer.SerializeToElement(new { status }),
                        BaseVersion: 0,
                        ClientTs: MorningOf.AddHours(1)),
                ]));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // Completed cannot go back to InProgress — legal as a target for the field workflow in
        // general (JobIntents.Drivable), illegal for this job's own state machine right now.
        using var illegal = await SendAsync(
            client,
            techToken,
            HttpMethod.Post,
            "/sync/push",
            new SyncPushRequest(
            [
                new SyncOp(
                    Guid.NewGuid(),
                    "job",
                    job.Id,
                    "status_change",
                    JsonSerializer.SerializeToElement(new { status = "InProgress" }),
                    BaseVersion: 0,
                    ClientTs: MorningOf.AddHours(2)),
            ]));
        Assert.Equal(HttpStatusCode.OK, illegal.StatusCode);

        var body = await illegal.Content.ReadFromJsonAsync<SyncPushResponse>();
        var conflict = Assert.Single(body!.Conflicts);
        Assert.Equal(SyncConflictReason.IllegalTransition, conflict.Reason);
        Assert.Empty(body.Applied);
    }

    /// <summary>
    /// The step-54 counters, over the whole real path: a push that applied one operation and
    /// refused another is counted once each, with the refusal carrying its reason.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is here, through HTTP, rather than in <c>MetricsFlowTests</c> through MediatR, because
    /// the counting moved. Recording inside the handler counted work the transaction could still
    /// take back — a batch applied and then lost to a failed save was reported as field work — so
    /// the numbers are now taken from a <c>Result</c> that has already committed, which only the
    /// edge holds.
    /// </para>
    /// <para>
    /// The refusal is the half that matters: it rides inside a 200 with no log level and no status
    /// code of its own, so this counter is the only thing standing between "a fleet of phones is
    /// being refused all morning" and a quiet dashboard.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task APushCountsWhatItAppliedAndWhyItRefusedTheRest()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();
        var adminToken = await client.LoginAsync(await SeedAdminAsync(org), "riverside-heating");
        var technicianId = await SeedTechnicianAsync(org);
        var techToken = await client.LoginAsync(
            await SeedTechnicianUserAsync(org, technicianId), "boiler-service-call");
        var job = await ABookedAndAssignedJobAsync(client, adminToken, technicianId.Value);

        using var applied = new MetricCollector<long>(
            _factory.Services.GetRequiredService<IMeterFactory>(),
            OpenDispatchMetrics.MeterName,
            SyncMetrics.OpsAppliedName);
        using var conflicted = new MetricCollector<long>(
            _factory.Services.GetRequiredService<IMeterFactory>(),
            OpenDispatchMetrics.MeterName,
            SyncMetrics.OpsConflictedName);

        using var response = await SendAsync(
            client,
            techToken,
            HttpMethod.Post,
            "/sync/push",
            new SyncPushRequest(
            [
                new SyncOp(
                    Guid.NewGuid(),
                    "job",
                    job.Id,
                    "status_change",
                    JsonSerializer.SerializeToElement(new { status = "EnRoute" }),
                    BaseVersion: 0,
                    ClientTs: MorningOf.AddHours(1)),

                // Legal for the field workflow in general, illegal from where this job now is:
                // EnRoute goes to InProgress or Cancelled, never straight to Completed.
                new SyncOp(
                    Guid.NewGuid(),
                    "job",
                    job.Id,
                    "status_change",
                    JsonSerializer.SerializeToElement(new { status = "Completed" }),
                    BaseVersion: 0,
                    ClientTs: MorningOf.AddHours(2)),
            ]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SyncPushResponse>();
        Assert.Single(body!.Applied);
        Assert.Single(body.Conflicts);

        Assert.Equal(1L, Assert.Single(applied.GetMeasurementSnapshot()).Value);

        var refusal = Assert.Single(conflicted.GetMeasurementSnapshot());
        Assert.Equal(1L, refusal.Value);
        Assert.Equal(JobErrors.IllegalTransitionCode, refusal.Tags[SyncMetrics.ConflictReasonTag]);
    }

    [Fact]
    public async Task ADispatcherCannotReachTheSyncSurface()
    {
        using var client = _factory.CreateClient();
        await _factory.SeedUserAsync(OrgId.New(), "dispatch@vance.example", "vance-refrigeration", UserRole.Dispatcher);
        var token = await client.LoginAsync("dispatch@vance.example", "vance-refrigeration");

        using var response = await SendAsync(client, token, HttpMethod.Get, "/sync/pull?since=0", body: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// A token this system did not issue in the usual way — a <c>Technician</c> seeded with no
    /// linked technician — meets the same bare 401 <c>TenantResolutionMiddleware</c> answers a
    /// missing org claim with, ahead of the <c>Result</c>/<c>Error</c> machinery entirely.
    /// </summary>
    [Fact]
    public async Task ATechnicianWithNoLinkedIdentityGetsABare401()
    {
        using var client = _factory.CreateClient();
        await _factory.SeedUserAsync(OrgId.New(), "field@vance.example", "boiler-service-call", UserRole.Technician);
        var token = await client.LoginAsync("field@vance.example", "boiler-service-call");

        using var response = await SendAsync(client, token, HttpMethod.Get, "/sync/pull?since=0", body: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> SeedAdminAsync(OrgId org)
    {
        const string username = "admin@riverside.example";
        await _factory.SeedUserAsync(org, username, "riverside-heating", UserRole.Admin);

        return username;
    }

    private async Task<string> SeedTechnicianUserAsync(OrgId org, TechnicianId technicianId)
    {
        const string username = "field@vance.example";
        await _factory.SeedUserAsync(org, username, "boiler-service-call", UserRole.Technician, technicianId);

        return username;
    }

    /// <summary>
    /// Writes a technician straight through EF — <c>POST /technicians</c> is <c>AdminOnly</c>, and
    /// this flow needs the id before it can seed the linked login anyway.
    /// </summary>
    private async Task<TechnicianId> SeedTechnicianAsync(OrgId org)
    {
        var technician = TechnicianBuilder.Any().ForOrg(org).Named("Alex Rivera").Skilled("hvac").Build();

        await using var context = _postgres.NewContext(org);
        context.Technicians.Add(technician);
        await context.SaveChangesAsync();

        return technician.Id;
    }

    private static async Task<JobResponse> ABookedAndAssignedJobAsync(
        HttpClient client, string adminToken, Guid technicianId)
    {
        var customer = await PostAsync<CustomerSummaryResponse>(
            client, adminToken, "/customers", new CreateCustomerRequest("Vance Refrigeration", null, null));
        var location = await PostAsync<ServiceLocationResponse>(
            client,
            adminToken,
            $"/customers/{customer.Id}/locations",
            new ServiceLocationRequest("Head office", "1 High Street, London", 51.5074d, -0.1278d));
        var job = await PostAsync<JobResponse>(
            client,
            adminToken,
            "/jobs",
            new CreateJobRequest(
                customer.Id, location.Id, "hvac", JobPriority.Normal,
                MorningOf.AddHours(1), MorningOf.AddHours(4), TimeSpan.FromHours(1)));

        using var assigned = await SendAsync(
            client, adminToken, HttpMethod.Post, $"/jobs/{job.Id}/assign",
            new AssignJobRequest(technicianId, MorningOf.AddHours(1)));
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        using var dispatched = await SendAsync(
            client, adminToken, HttpMethod.Post, $"/jobs/{job.Id}/status",
            new ChangeJobStatusRequest(JobStatus.Dispatched));
        Assert.Equal(HttpStatusCode.NoContent, dispatched.StatusCode);

        return job;
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
