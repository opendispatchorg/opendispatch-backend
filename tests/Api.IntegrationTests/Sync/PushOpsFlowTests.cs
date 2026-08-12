using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Sync;
using OpenDispatch.Application.Sync.PushOps;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Sync;

/// <summary>
/// A push against a real database, through the real pipeline.
/// </summary>
/// <remarks>
/// The unit tests settle what the handler decides. Four things only Postgres can settle, and each
/// is a claim the fakes are structurally unable to make: that a batch carrying a conflict still
/// <em>commits</em> rather than being rolled back with it, that the op log dedupes across
/// requests rather than within one, that a recorded note or part moves the job's version, and that
/// the cursor a push hands back finds the batch's own changes.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class PushOpsFlowTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);
    private static readonly TechnicianId Technician = TechnicianId.New();

    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public PushOpsFlowTests(PostgresFixture postgres) => _postgres = postgres;

    /// <summary>
    /// A morning in a dead spot, arriving at once: the job moves, what the technician found and
    /// what the work took are on it, and the log has a row per operation so that none of it can
    /// happen twice.
    /// </summary>
    [Fact]
    public async Task LandsAWholeMorningAndWritesTheLogInTheSameTransaction()
    {
        await using var services = BuildHost();
        var job = await ADispatchedJobAsync(services);

        var pushed = await Send(services, new PushOpsCommand(
            Technician,
            [
                Status(job, JobStatus.EnRoute, MondayMorning.AddMinutes(5)),
                Status(job, JobStatus.InProgress, MondayMorning.AddMinutes(30)),
                Note(job, "Meter behind the boiler; access via side gate.", MondayMorning.AddMinutes(35)),
                Line(job, LineItemKind.Part, "Run capacitor 45/5", 2m, 2_850, MondayMorning.AddMinutes(50)),
                Status(job, JobStatus.Completed, MondayMorning.AddMinutes(95)),
            ]));

        Assert.True(pushed.IsSuccess);
        Assert.Equal(5, pushed.Value.Applied.Count);
        Assert.Empty(pushed.Value.Conflicts);

        await using var context = _postgres.NewContext(_tenant);
        var worked = await context.Jobs.SingleAsync(candidate => candidate.Id == job);

        Assert.Equal(JobStatus.Completed, worked.Status);
        Assert.Equal("Meter behind the boiler; access via side gate.", worked.Notes);
        Assert.Equal(MondayMorning.AddMinutes(35), worked.NotesRecordedAt);
        Assert.Equal(5_700L, Assert.Single(worked.Lines).LineTotal.Cents);

        var logged = await context.SyncOps.ToListAsync();
        Assert.Equal(5, logged.Count);
        Assert.All(logged, op => Assert.Equal(Technician, op.TechnicianId));
        Assert.All(logged, op => Assert.Equal(job.Value, op.EntityId));

        // The payload went into jsonb as the device wrote it, and comes back readable.
        var note = Assert.Single(logged, op => op.Type == FieldOps.AddNote);
        using var payload = JsonDocument.Parse(note.Payload);
        Assert.Equal(
            "Meter behind the boiler; access via side gate.",
            payload.RootElement.GetProperty("text").GetString());
    }

    /// <summary>
    /// The connection dropped before the response arrived, so the device sends the batch again.
    /// Across two requests and two transactions, which is the arrangement the op log exists for —
    /// the second push reads what the first committed.
    /// </summary>
    [Fact]
    public async Task ReplayingABatchAcrossTwoRequestsAppliesNothingTwice()
    {
        await using var services = BuildHost();
        var job = await ADispatchedJobAsync(services);
        var ops = new[]
        {
            Status(job, JobStatus.EnRoute, MondayMorning),
            Line(job, LineItemKind.Labor, "Diagnostic", 1m, 9_500, MondayMorning.AddMinutes(20)),
        };

        var first = await Send(services, new PushOpsCommand(Technician, ops));
        var again = await Send(services, new PushOpsCommand(Technician, ops));

        Assert.Equal(first.Value.Applied, again.Value.Applied);
        Assert.Empty(again.Value.Conflicts);

        await using var context = _postgres.NewContext(_tenant);
        var worked = await context.Jobs.SingleAsync(candidate => candidate.Id == job);

        Assert.Equal(JobStatus.EnRoute, worked.Status);
        Assert.Single(worked.Lines);
        Assert.Equal(2, await context.SyncOps.CountAsync());
    }

    /// <summary>
    /// The claim the fakes cannot make. <c>TransactionBehavior</c> rolls back a failed result, so
    /// a push that reported a refusal as a failure would take the operations that <em>did</em>
    /// apply down with it — and the only way to see the difference is a real transaction that has
    /// really committed.
    /// </summary>
    [Fact]
    public async Task CommitsTheOperationsThatAppliedEvenWhenOneIsRefused()
    {
        await using var services = BuildHost();
        var job = await ADispatchedJobAsync(services);

        var pushed = await Send(services, new PushOpsCommand(
            Technician,
            [
                Note(job, "Customer let me in.", MondayMorning),
                Status(job, JobStatus.Completed, MondayMorning.AddMinutes(10)),
                Line(job, LineItemKind.Labor, "Callout", 1m, 8_000, MondayMorning.AddMinutes(20)),
            ]));

        Assert.True(pushed.IsSuccess);
        Assert.Equal(JobErrors.IllegalTransitionCode, Assert.Single(pushed.Value.Conflicts).Error.Code);

        await using var context = _postgres.NewContext(_tenant);
        var worked = await context.Jobs.SingleAsync(candidate => candidate.Id == job);

        Assert.Equal("Customer let me in.", worked.Notes);
        Assert.Single(worked.Lines);
        Assert.Equal(JobStatus.Dispatched, worked.Status);

        // Two rows, not three: the refused operation is not in the log, so re-sending it will be
        // judged against the job again rather than answered from memory.
        Assert.Equal(2, await context.SyncOps.CountAsync());
    }

    /// <summary>
    /// The step's "bump <c>Version</c>" for field operations, which is not something the handler
    /// does: recording a note or a part changes the job's own row, so the persistence layer stamps
    /// it like any other change. A device rebasing reads that version back.
    /// </summary>
    [Fact]
    public async Task MovesTheJobsVersionWhenTheFieldRecordsSomething()
    {
        await using var services = BuildHost();
        var job = await ADispatchedJobAsync(services);

        long versionBefore;
        await using (var before = _postgres.NewContext(_tenant))
        {
            versionBefore = (await before.Jobs.SingleAsync(candidate => candidate.Id == job)).Version;
        }

        await Send(services, new PushOpsCommand(
            Technician,
            [Line(job, LineItemKind.Part, "Filter", 1m, 1_200, MondayMorning)]));

        await using var after = _postgres.NewContext(_tenant);
        var worked = await after.Jobs.SingleAsync(candidate => candidate.Id == job);

        Assert.True(
            worked.Version > versionBefore,
            $"a recorded line left the version at {worked.Version}");
    }

    /// <summary>
    /// A push hands back a cursor taken before the batch commits, so pulling with it returns the
    /// batch's own effects. That is what makes the server authoritative in practice: a device with
    /// a conflict is told what was refused, and finds out what is true by pulling.
    /// </summary>
    [Fact]
    public async Task HandsBackACursorThatStillFindsTheBatchItJustApplied()
    {
        await using var services = BuildHost();
        var job = await ADispatchedJobAsync(services);

        var pushed = await Send(services, new PushOpsCommand(
            Technician,
            [Note(job, "Back in the morning for the part.", MondayMorning)]));

        await using var context = _postgres.NewContext(_tenant);
        var changed = await context.Jobs
            .Where(candidate => EF.Property<long>(candidate, "ChangeSeq") >= pushed.Value.Cursor.Value)
            .Select(candidate => candidate.Id)
            .ToListAsync();

        Assert.Contains(job, changed);
    }

    /// <summary>
    /// One organization's device cannot touch another's work, and the answer it gets is "no such
    /// job" rather than a refusal that would confirm the job exists.
    /// </summary>
    [Fact]
    public async Task RefusesAnOperationForAnotherOrganizationsJob()
    {
        await using var services = BuildHost();
        var job = await ADispatchedJobAsync(services);

        using var elsewhere = services.ActingAs(OrgId.New());
        var pushed = await elsewhere.ServiceProvider
            .GetRequiredService<ISender>()
            .Send(new PushOpsCommand(Technician, [Status(job, JobStatus.EnRoute, MondayMorning)]));

        Assert.Equal(JobErrors.NotFoundCode, Assert.Single(pushed.Value.Conflicts).Error.Code);

        await using var context = _postgres.NewContext(_tenant);
        Assert.Equal(JobStatus.Dispatched, (await context.Jobs.SingleAsync(c => c.Id == job)).Status);
        Assert.Empty(await context.SyncOps.ToListAsync());
    }

    private static PushedOp Status(JobId job, JobStatus status, DateTimeOffset at) =>
        Op(job, FieldOps.StatusChange, $$"""{"status":"{{status}}"}""", at);

    private static PushedOp Note(JobId job, string text, DateTimeOffset at) =>
        Op(job, FieldOps.AddNote, JsonSerializer.Serialize(new { text }), at);

    private static PushedOp Line(
        JobId job,
        LineItemKind kind,
        string description,
        decimal quantity,
        long unitPriceCents,
        DateTimeOffset at) =>
        Op(
            job,
            FieldOps.AddLineItem,
            JsonSerializer.Serialize(new { kind = kind.ToString(), description, quantity, unitPriceCents }),
            at);

    private static PushedOp Op(JobId job, string type, string payload, DateTimeOffset at) =>
        new(
            SyncOpId.From(Guid.NewGuid()),
            FieldOps.JobEntity,
            job.Value,
            type,
            JsonDocument.Parse(payload).RootElement,
            BaseVersion: 1,
            ClientTs: at);

    private async Task<JobId> ADispatchedJobAsync(ServiceProvider services)
    {
        var customer = await Send(services, new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await Send(services, new AddServiceLocationCommand(
            customer.Value,
            "Head office",
            "12 Bath Road, Slough",
            51.5107,
            -0.5950));

        var job = await Send(services, new CreateJobCommand(
            customer.Value,
            location.Value,
            "hvac",
            JobPriority.Normal,
            MondayMorning,
            MondayMorning.AddHours(8),
            TimeSpan.FromHours(1)));

        await Send(services, new ChangeJobStatusCommand(job.Value, JobStatus.Scheduled));
        await Send(services, new ChangeJobStatusCommand(job.Value, JobStatus.Dispatched));

        return job.Value;
    }

    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);

    /// <summary>One request: one scope, one tenant, one unit of work.</summary>
    private async Task<TResponse> Send<TResponse>(ServiceProvider services, IRequest<TResponse> request)
    {
        using var scope = services.ActingAs(_tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}
