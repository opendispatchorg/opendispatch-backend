using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Auth;
using OpenDispatch.Contracts;
using OpenDispatch.Contracts.Board;
using OpenDispatch.Contracts.Customers;
using OpenDispatch.Contracts.Export;
using OpenDispatch.Contracts.Invoicing;
using OpenDispatch.Contracts.Jobs;
using OpenDispatch.Contracts.Schedule;
using OpenDispatch.Contracts.Sync;
using OpenDispatch.Contracts.Technicians;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.EndToEnd;

/// <summary>
/// The whole loop, once, over HTTP (Document 3, step 55): a customer calls, the day is planned, a
/// technician drives it from a phone, and the work becomes a paid invoice that the business can
/// export.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One test, deliberately.</strong> Every stage here has its own focused coverage already —
/// the optimiser against Postgres, the push/pull mechanics, the conflict policy, the invoice
/// arithmetic, each endpoint's authorization. What none of them can catch is the loop coming apart
/// <em>between</em> those stages: a status the field can reach but invoicing will not accept, an
/// assignment the board shows and the phone cannot see, a note that never leaves the sync table.
/// This is the regression guard for the seams, so it asserts state after every stage rather than
/// only at the end, and it is written as one narrative because that is what it is testing.
/// </para>
/// <para>
/// <strong>Over HTTP, through three real logins.</strong> The loop is not a set of handlers; it is
/// what an office browser and a technician's phone can actually do, and the role split is part of
/// the loop's shape — the dispatcher plans, the technician syncs, and only the admin bills. Sending
/// commands through MediatR would exercise the same handlers while skipping the half of this that
/// has ever broken.
/// </para>
/// <para>
/// The day is in the past so the server's clamp on a device's clock (<c>PushOpsHandler.Observed</c>)
/// keeps the timestamps the phone sends, exactly as it would for work done this morning and pushed
/// this afternoon.
/// </para>
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class CallToCashFlowTests : IClassFixture<ApiFactory>
{
    private static readonly DateOnly Day = new(2026, 8, 10);
    private static readonly DateTimeOffset ShiftStart = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ShiftEnd = new(2026, 8, 10, 17, 0, 0, TimeSpan.Zero);

    private const string AdminPassword = "riverside-heating";
    private const string DispatcherPassword = "vance-refrigeration";
    private const string TechnicianPassword = "boiler-service-call";

    private readonly ApiFactory _factory;

    public CallToCashFlowTests(ApiFactory factory, PostgresFixture postgres)
    {
        _factory = factory;
        _factory.ConnectionString = postgres.ConnectionString;
    }

    [Fact]
    public async Task ACustomerCallBecomesAPaidInvoiceTheBusinessCanExport()
    {
        var org = OrgId.New();
        using var client = _factory.CreateClient();

        var admin = await LoginAsync(client, org, "admin", AdminPassword, UserRole.Admin);
        var dispatcher = await LoginAsync(client, org, "dispatch", DispatcherPassword, UserRole.Dispatcher);

        // ── The crew ────────────────────────────────────────────────────────────────────────
        // Admin-only, and the technician's login can only be issued once the technician exists:
        // /sync scopes every answer by the TechnicianId on the token.
        var technician = await PostAsync<TechnicianResponse>(
            client,
            admin,
            "/technicians",
            new CreateTechnicianRequest("Marisol Ruiz", ["hvac"], ShiftStart, ShiftEnd, 45.5152d, -122.6784d),
            HttpStatusCode.Created);

        var phone = await LoginAsync(
            client, org, "marisol", TechnicianPassword, UserRole.Technician, TechnicianId.From(technician.Id));

        // ── Stage 1: the call ───────────────────────────────────────────────────────────────
        var customer = await PostAsync<CustomerSummaryResponse>(
            client,
            dispatcher,
            "/customers",
            new CreateCustomerRequest("Ada Whitlock", "ada@whitlock.example", "+1-503-555-0148"),
            HttpStatusCode.Created);

        var location = await PostAsync<ServiceLocationResponse>(
            client,
            dispatcher,
            $"/customers/{customer.Id}/locations",
            new ServiceLocationRequest("Home", "1932 SE Ladd Ave, Portland", 45.5051d, -122.6520d),
            HttpStatusCode.Created);

        var booked = await PostAsync<JobResponse>(
            client,
            dispatcher,
            "/jobs",
            new CreateJobRequest(
                customer.Id,
                location.Id,
                "hvac",
                JobPriority.Normal,
                ShiftStart.AddHours(1),
                ShiftStart.AddHours(4),
                TimeSpan.FromMinutes(90)),
            HttpStatusCode.Created);

        // Demand, not plan: a booked job is nobody's work yet.
        Assert.Equal(JobStatus.Unscheduled, booked.Status);

        // ── Stage 2: the day is planned ─────────────────────────────────────────────────────
        var optimized = await PostAsync<OptimizeScheduleResponse>(
            client,
            dispatcher,
            "/schedule/optimize",
            new OptimizeScheduleRequest(ShiftStart, ShiftEnd));

        Assert.Equal(1, optimized.Planned);
        Assert.Empty(optimized.Unassigned);
        Assert.Equal(JobStatus.Scheduled, (await JobAsync(client, dispatcher, booked.Id)).Status);

        var planned = await GetAsync<DispatchBoardResponse>(
            client, dispatcher, $"/dispatch/board?day={Day:yyyy-MM-dd}");
        var plannedStop = Assert.Single(Assert.Single(planned.Routes).Stops);

        Assert.Equal(booked.Id, plannedStop.Job.JobId);
        Assert.Equal(TimeSpan.Zero, plannedStop.LateBy);

        // ── Stage 3: the dispatcher moves it by hand ────────────────────────────────────────
        // The manual override the board exists for. The plan is rewritten, not added to — same
        // assignment, new time — which is the whole reason Job and Assignment are separate.
        var movedTo = ShiftStart.AddHours(2);
        var assigned = await PostAsync<AssignJobResponse>(
            client,
            dispatcher,
            $"/jobs/{booked.Id}/assign",
            new AssignJobRequest(technician.Id, movedTo));

        Assert.Equal(plannedStop.AssignmentId, assigned.AssignmentId);

        var afterDrag = await GetAsync<DispatchBoardResponse>(
            client, dispatcher, $"/dispatch/board?day={Day:yyyy-MM-dd}");
        var draggedStop = Assert.Single(Assert.Single(afterDrag.Routes).Stops);

        Assert.Equal(assigned.AssignmentId, draggedStop.AssignmentId);
        Assert.Equal(movedTo, draggedStop.ScheduledStart);

        // ── Stage 4: dispatched to the phone ────────────────────────────────────────────────
        await PostAsync(
            client,
            dispatcher,
            $"/jobs/{booked.Id}/status",
            new ChangeJobStatusRequest(JobStatus.Dispatched),
            HttpStatusCode.NoContent);

        Assert.Equal(JobStatus.Dispatched, (await JobAsync(client, dispatcher, booked.Id)).Status);

        // ── Stage 5: the phone pulls its day ────────────────────────────────────────────────
        var firstPull = await GetAsync<SyncPullResponse>(client, phone, "/sync/pull?since=0");
        var pulledJob = Payload<SyncJobPayload>(
            Assert.Single(firstPull.Changes, change => change.Entity == "job" && change.EntityId == booked.Id));
        var pulledStop = Payload<SyncStopPayload>(
            Assert.Single(firstPull.Changes, change => change.Entity == "assignment"));

        Assert.Equal(JobStatus.Dispatched, pulledJob.Status);
        Assert.Equal("Ada Whitlock", pulledJob.CustomerName);
        Assert.Equal("1932 SE Ladd Ave, Portland", pulledJob.Address);
        Assert.Equal(booked.Id, pulledStop.JobId);
        Assert.Equal(movedTo, pulledStop.ScheduledStart);

        // ── Stage 6: a morning's work, pushed in one batch ──────────────────────────────────
        // What a dead spot produces: five operations recorded on the phone and sent when the
        // signal comes back, in the order they happened.
        var ops = new[]
        {
            Op(booked.Id, "status_change", new { status = "EnRoute" }, movedTo),
            Op(booked.Id, "status_change", new { status = "InProgress" }, movedTo.AddMinutes(18)),
            Op(booked.Id, "add_note", new { text = "Blower wheel packed with debris; cleaned and rebalanced." }, movedTo.AddMinutes(50)),
            Op(
                booked.Id,
                "add_line_item",
                new { kind = "Part", description = "Run capacitor 45/5", quantity = 1, unitPriceCents = 2_850 },
                movedTo.AddMinutes(55)),
            Op(
                booked.Id,
                "add_line_item",
                new { kind = "Labor", description = "Diagnostic and clean", quantity = 1.5m, unitPriceCents = 9_500 },
                movedTo.AddMinutes(80)),
            Op(booked.Id, "status_change", new { status = "Completed" }, movedTo.AddMinutes(85)),
        };

        var pushed = await PostAsync<SyncPushResponse>(
            client, phone, "/sync/push", new SyncPushRequest([.. ops]));

        Assert.Equal(ops.Length, pushed.Applied.Count);
        Assert.Empty(pushed.Conflicts);

        // ── Stage 7: the server's answer is what the phone rebases onto ─────────────────────
        var secondPull = await GetAsync<SyncPullResponse>(client, phone, $"/sync/pull?since={pushed.Cursor}");
        var finishedJob = Payload<SyncJobPayload>(
            Assert.Single(secondPull.Changes, change => change.Entity == "job" && change.EntityId == booked.Id));

        Assert.Equal(JobStatus.Completed, finishedJob.Status);
        Assert.Equal("Blower wheel packed with debris; cleaned and rebalanced.", finishedJob.Notes);
        Assert.Equal(2, finishedJob.Lines.Count);

        // The office sees the same field work through its own endpoint, which is the seam this
        // whole test exists for: nothing is stranded in the sync tables.
        Assert.Equal(finishedJob.Notes, (await JobAsync(client, dispatcher, booked.Id)).Notes);

        // ── Stage 8: the bill, raised from what the technician recorded ─────────────────────
        var invoice = await PostAsync<InvoiceResponse>(
            client,
            admin,
            $"/jobs/{booked.Id}/invoice",
            new CreateInvoiceRequest(
                [.. finishedJob.Lines.Select(line =>
                    new InvoiceLineRequest(line.Kind, line.Description, line.Quantity, line.UnitPrice))]),
            HttpStatusCode.Created);

        // 28.50 for the capacitor, 1.5 hours at 95.00 for the labour.
        Assert.Equal(InvoiceStatus.Draft, invoice.Status);
        Assert.Equal(171.00m, invoice.Total);
        Assert.Equal(JobStatus.Invoiced, (await JobAsync(client, dispatcher, booked.Id)).Status);

        // ── Stage 9: paid ──────────────────────────────────────────────────────────────────
        await PostAsync(client, admin, $"/invoices/{invoice.Id}/pay", body: null, HttpStatusCode.NoContent);

        Assert.Equal(JobStatus.Paid, (await JobAsync(client, dispatcher, booked.Id)).Status);

        // ── Stage 10: the business owns all of it ──────────────────────────────────────────
        // Document 1's anti-lock-in promise, and the only assertion here that reads the whole
        // tenant at once: every aggregate the loop touched, in one dump.
        var export = await GetAsync<ExportResponse>(client, admin, "/export");

        var exportedCustomer = Assert.Single(export.Customers);
        Assert.Equal("Ada Whitlock", exportedCustomer.Name);
        Assert.Equal(location.Id, Assert.Single(exportedCustomer.Locations).Id);

        var exportedJob = Assert.Single(export.Jobs);
        Assert.Equal(JobStatus.Paid, exportedJob.Status);
        Assert.Equal(finishedJob.Notes, exportedJob.Notes);

        var exportedAssignment = Assert.Single(export.Assignments);
        Assert.Equal(technician.Id, exportedAssignment.TechnicianId);
        Assert.Equal(movedTo, exportedAssignment.ScheduledStart);

        var exportedInvoice = Assert.Single(export.Invoices);
        Assert.Equal(InvoiceStatus.Paid, exportedInvoice.Status);
        Assert.Equal(171.00m, exportedInvoice.Total);
        Assert.Empty(export.Attachments);
    }

    private static SyncOp Op(Guid jobId, string type, object payload, DateTimeOffset at) => new(
        Guid.NewGuid(),
        "job",
        jobId,
        type,
        JsonSerializer.SerializeToElement(payload),
        BaseVersion: 0,
        ClientTs: at);

    private static TPayload Payload<TPayload>(SyncChange change) =>
        change.State!.Value.Deserialize<TPayload>()!;

    /// <summary>Seeds one login and signs in through the real endpoint, returning its token.</summary>
    private async Task<string> LoginAsync(
        HttpClient client,
        OrgId org,
        string username,
        string password,
        UserRole role,
        TechnicianId? technicianId = null)
    {
        // Unique per run: the user store is a host-wide singleton shared with every other test
        // class using this factory, and a duplicate username is rejected.
        var unique = $"{username}-{Guid.NewGuid():N}@call-to-cash.example";
        await _factory.SeedUserAsync(org, unique, password, role, technicianId);

        return await client.LoginAsync(unique, password);
    }

    private static Task<JobResponse> JobAsync(HttpClient client, string token, Guid jobId) =>
        GetAsync<JobResponse>(client, token, $"/jobs/{jobId}");

    private static async Task<TResponse> GetAsync<TResponse>(HttpClient client, string token, string path)
    {
        using var response = await SendAsync(client, token, HttpMethod.Get, path, body: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<TResponse>())!;
    }

    private static async Task<TResponse> PostAsync<TResponse>(
        HttpClient client,
        string token,
        string path,
        object body,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await SendAsync(client, token, HttpMethod.Post, path, body);

        await AssertStatusAsync(response, expected);

        return (await response.Content.ReadFromJsonAsync<TResponse>())!;
    }

    private static async Task PostAsync(
        HttpClient client,
        string token,
        string path,
        object? body,
        HttpStatusCode expected)
    {
        using var response = await SendAsync(client, token, HttpMethod.Post, path, body);

        await AssertStatusAsync(response, expected);
    }

    /// <summary>
    /// Fails with the server's own ProblemDetails body rather than with a bare status code — in a
    /// ten-stage flow, "expected Created, got Conflict" without the reason is a debugging session.
    /// </summary>
    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode != expected)
        {
            var body = await response.Content.ReadAsStringAsync();

            Assert.Fail($"{response.RequestMessage?.RequestUri} answered {(int)response.StatusCode}, expected {(int)expected}: {body}");
        }
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
