using System.Text.Json;
using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Results;
using OpenDispatch.Application.Sync;
using OpenDispatch.Application.Sync.PushOps;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Sync;

/// <summary>
/// A technician's queue arriving at the server.
/// </summary>
/// <remarks>
/// <para>
/// The half of sync that has to be right (TESTING.md), so this is dense on purpose. What it holds
/// down: an operation applies once however many times it is sent, a refusal costs one operation
/// rather than the batch around it, the state machine governs status rather than arrival order,
/// and free text is decided by when the technician wrote it rather than by when it landed.
/// </para>
/// <para>
/// The domain rules themselves — which transitions are legal, what makes a note newer — are tested
/// in <c>Domain.Tests</c> and are not restated here. What this adds is the replay: which rule is
/// asked, what happens to the rest of the batch when the answer is no, and what the device is told.
/// </para>
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class PushOpsTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly TechnicianId Technician = TechnicianId.New();

    /// <summary>
    /// The case the batch exists for: a technician who worked through a dead spot pushes the whole
    /// morning at once, and it lands as the sequence they performed rather than as a jump from
    /// dispatched to done.
    /// </summary>
    [Fact]
    public async Task ReplaysAWholeMorningInTheOrderItHappened()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);

        var pushed = await slice.Send(new PushOpsCommand(
            Technician,
            [
                Status(job, JobStatus.EnRoute, MondayMorning.AddMinutes(5)),
                Status(job, JobStatus.InProgress, MondayMorning.AddMinutes(25)),
                Note(job, "Meter behind the boiler.", MondayMorning.AddMinutes(40)),
                Line(job, LineItemKind.Part, "Run capacitor", 2m, 2_850, MondayMorning.AddMinutes(45)),
                Status(job, JobStatus.Completed, MondayMorning.AddMinutes(90)),
            ]));

        var batch = pushed.Value;
        Assert.Empty(batch.Conflicts);
        Assert.Equal(5, batch.Applied.Count);

        var worked = Saved(slice);
        Assert.Equal(JobStatus.Completed, worked.Status);
        Assert.Equal("Meter behind the boiler.", worked.Notes);
        Assert.Equal(2_850L, Assert.Single(worked.Lines).UnitPrice.Cents);

        // The events are the domain's, raised by the same intent methods the REST path calls —
        // the last three, after the two that planning the job raised.
        Assert.Equal(
            [typeof(JobEnRoute), typeof(JobInProgress), typeof(JobCompleted)],
            worked.DomainEvents.TakeLast(3).Select(raised => raised.GetType()));
    }

    /// <summary>
    /// The step's first requirement, and the one a flaky connection produces daily: the response to
    /// a push is lost, so the device sends the batch again.
    /// </summary>
    [Fact]
    public async Task ReplayingABatchChangesNothingAndStillReportsItApplied()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);
        var ops = new[]
        {
            Status(job, JobStatus.EnRoute, MondayMorning),
            Line(job, LineItemKind.Labor, "Diagnostic", 1m, 9_500, MondayMorning.AddMinutes(20)),
        };

        var first = await slice.Send(new PushOpsCommand(Technician, ops));
        var again = await slice.Send(new PushOpsCommand(Technician, ops));

        // Reported applied both times: from the device's point of view a re-sent operation that is
        // already in effect succeeded, and any other answer leaves it queueing forever.
        Assert.Equal(first.Value.Applied, again.Value.Applied);
        Assert.Empty(again.Value.Conflicts);

        var worked = Saved(slice);
        Assert.Equal(JobStatus.EnRoute, worked.Status);
        Assert.Single(worked.Lines);
        Assert.Single(slice.Store<SyncOpRecord>().Saved, op => op.Type == FieldOps.AddLineItem);
    }

    /// <summary>
    /// The same thing within one request rather than across two. A device that wrote an operation
    /// down twice performed it once, so it is applied once and named once.
    /// </summary>
    [Fact]
    public async Task AppliesAnOperationSentTwiceInOneBatchOnce()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);
        var line = Line(job, LineItemKind.Part, "Filter", 1m, 1_200, MondayMorning);

        var pushed = await slice.Send(new PushOpsCommand(Technician, [line, line]));

        Assert.Equal([line.Id], pushed.Value.Applied);
        Assert.Empty(pushed.Value.Conflicts);
        Assert.Single(Saved(slice).Lines);
    }

    /// <summary>
    /// The decision this slice is shaped around, and the one step 30 left open: a batch that
    /// applied four operations and refused the fifth keeps the four. A failure result would roll
    /// the transaction back and lose a morning's work over one stale tap.
    /// </summary>
    [Fact]
    public async Task RefusingOneOperationKeepsTheRestOfTheBatch()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);
        var illegal = Status(job, JobStatus.Completed, MondayMorning.AddMinutes(10));

        var pushed = await slice.Send(new PushOpsCommand(
            Technician,
            [
                Note(job, "Customer let me in.", MondayMorning),
                illegal,
                Line(job, LineItemKind.Labor, "Callout", 1m, 8_000, MondayMorning.AddMinutes(20)),
            ]));

        // A success carrying conflicts, not a failure. The pipeline commits on success, so this is
        // what keeps the other two operations.
        Assert.True(pushed.IsSuccess);
        Assert.Equal(2, pushed.Value.Applied.Count);

        var conflict = Assert.Single(pushed.Value.Conflicts);
        Assert.Equal(illegal.Id, conflict.OpId);
        Assert.Equal(JobErrors.IllegalTransitionCode, conflict.Error.Code);
        Assert.Equal(ErrorCategory.Conflict, conflict.Error.Category);

        var worked = Saved(slice);
        Assert.Equal("Customer let me in.", worked.Notes);
        Assert.Single(worked.Lines);

        // Untouched by the operation that was refused — a conflict reports, it does not half-apply.
        Assert.Equal(JobStatus.Dispatched, worked.Status);
    }

    /// <summary>
    /// A stale phone cannot push a job somebody else finished with back into the middle of its
    /// day. Legality is the rule, not who wrote last (Document 2 §10).
    /// </summary>
    [Fact]
    public async Task JudgesAStatusOperationByTheStateMachineRatherThanByWhoWroteLast()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);
        await slice.Send(new ChangeJobStatusCommand(job, JobStatus.Cancelled));

        var pushed = await slice.Send(new PushOpsCommand(
            Technician,
            [Status(job, JobStatus.EnRoute, MondayMorning.AddHours(1))]));

        var conflict = Assert.Single(pushed.Value.Conflicts);
        Assert.Equal(JobErrors.IllegalTransitionCode, conflict.Error.Code);

        // Both ends named, the same sentence the REST endpoint would have given.
        Assert.Contains("Cancelled", conflict.Error.Message, StringComparison.Ordinal);
        Assert.Contains("EnRoute", conflict.Error.Message, StringComparison.Ordinal);
        Assert.Equal(JobStatus.Cancelled, Saved(slice).Status);
    }

    /// <summary>
    /// A legal move from a device that was looking at an older version is still applied. Versions
    /// do not govern status — the state machine does — so a phone that has been in a basement all
    /// morning can still say it set off.
    /// </summary>
    [Fact]
    public async Task AppliesALegalStatusChangeFromADeviceWorkingFromAStaleVersion()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);

        var pushed = await slice.Send(new PushOpsCommand(
            Technician,
            [Status(job, JobStatus.EnRoute, MondayMorning) with { BaseVersion = 0 }]));

        Assert.Empty(pushed.Value.Conflicts);
        Assert.Equal(JobStatus.EnRoute, Saved(slice).Status);
    }

    /// <summary>
    /// The step's "concurrent field edits resolve to a defined result": free text is
    /// last-write-wins by the writer's own clock, so a note written at nine does not overwrite one
    /// written at noon just by arriving in the evening.
    /// </summary>
    [Fact]
    public async Task KeepsTheNoteThatWasWrittenLastRatherThanTheOneThatArrivedLast()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);
        var noon = MondayMorning.AddHours(3);

        await slice.Send(new PushOpsCommand(Technician, [Note(job, "Office: customer rang.", noon)]));

        var stale = Note(job, "Nobody home.", MondayMorning);
        var pushed = await slice.Send(new PushOpsCommand(Technician, [stale]));

        var conflict = Assert.Single(pushed.Value.Conflicts);
        Assert.Equal(stale.Id, conflict.OpId);
        Assert.Equal(SyncErrors.NotesSupersededCode, conflict.Error.Code);

        // The server's copy stands, which is the whole of the conflict policy: the device rebases
        // onto this by pulling with the cursor it was just handed.
        Assert.Equal("Office: customer rang.", Saved(slice).Notes);
    }

    /// <summary>
    /// The hole last-write-wins leaves open: a phone whose clock is a day fast would beat every
    /// other writer until that date passed, and nothing would report it. An operation from the
    /// future is believed as far as "now" and no further.
    /// </summary>
    [Fact]
    public async Task RefusesToBelieveAnOperationFromTheFuture()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);
        slice.Clock.UtcNow = MondayMorning.AddHours(4);

        // The office writes now; the phone claims tomorrow.
        await slice.Send(new PushOpsCommand(Technician, [Note(job, "Office: rang ahead.", slice.Clock.UtcNow)]));

        var fromTheFuture = Note(job, "Nobody home.", slice.Clock.UtcNow.AddDays(1));
        var pushed = await slice.Send(new PushOpsCommand(Technician, [fromTheFuture]));

        // Clamped to now, which loses the tie against the note already recorded at now — so the
        // fast clock wins nothing rather than everything.
        Assert.Equal(SyncErrors.NotesSupersededCode, Assert.Single(pushed.Value.Conflicts).Error.Code);
        Assert.Equal("Office: rang ahead.", Saved(slice).Notes);
    }

    /// <summary>
    /// The honest half of the rule, unchanged: a technician who wrote a note in a basement at nine
    /// wrote it at nine, whatever time it reaches the server.
    /// </summary>
    [Fact]
    public async Task BelievesAnOperationFromThePast()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);
        slice.Clock.UtcNow = MondayMorning.AddHours(9);

        await slice.Send(new PushOpsCommand(Technician, [Note(job, "In the crawlspace.", MondayMorning)]));

        var worked = Saved(slice);
        Assert.Equal("In the crawlspace.", worked.Notes);
        Assert.Equal(MondayMorning, worked.NotesRecordedAt);
    }

    /// <summary>
    /// The clamp applies to what the domain is told, not to what the log records. A row that had
    /// been clamped would hide the broken clock instead of being the one place it can be found.
    /// </summary>
    [Fact]
    public async Task RecordsWhatTheDeviceClaimedEvenWhenItIsNotBelieved()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);
        slice.Clock.UtcNow = MondayMorning;
        var tomorrow = MondayMorning.AddDays(1);

        await slice.Send(new PushOpsCommand(Technician, [Note(job, "Ahead of itself.", tomorrow)]));

        Assert.Equal(tomorrow, Assert.Single(slice.Store<SyncOpRecord>().Saved).ClientTs);
        Assert.Equal(MondayMorning, Saved(slice).NotesRecordedAt);
    }

    /// <summary>
    /// A completion dated tomorrow is the same broken clock arriving by a different field, so the
    /// payload's own instant is clamped too — otherwise the work lands in the future on the
    /// invoice and in every number the day is measured by.
    /// </summary>
    [Fact]
    public async Task RefusesToBelieveWorkFinishedInTheFuture()
    {
        await using var slice = SliceHost.Sync();
        var job = await AJobInProgress(slice);
        slice.Clock.UtcNow = MondayMorning.AddHours(3);

        await slice.Send(new PushOpsCommand(
            Technician,
            [
                Op(job, FieldOps.StatusChange, $$"""
                    {"status":"Completed","completedAt":"{{MondayMorning.AddDays(1):O}}"}
                    """, MondayMorning.AddHours(2)),
            ]));

        Assert.Equal(
            slice.Clock.UtcNow,
            Assert.IsType<JobCompleted>(Saved(slice).DomainEvents[^1]).OccurredAt);
    }

    [Fact]
    public async Task KeepsANoteWrittenAfterTheOneItAlreadyHolds()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);

        await slice.Send(new PushOpsCommand(Technician, [Note(job, "First look.", MondayMorning)]));
        var pushed = await slice.Send(new PushOpsCommand(
            Technician,
            [Note(job, "Second look — needs a part.", MondayMorning.AddHours(1))]));

        Assert.Empty(pushed.Value.Conflicts);
        Assert.Equal("Second look — needs a part.", Saved(slice).Notes);
    }

    /// <summary>
    /// Two parts recorded by two people are two parts. An append has nothing to conflict with,
    /// which is why last-write-wins is a rule about free text and not about everything.
    /// </summary>
    [Fact]
    public async Task RecordsEveryLineRatherThanLettingTheLastOneWin()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);

        await slice.Send(new PushOpsCommand(
            Technician,
            [
                Line(job, LineItemKind.Part, "Capacitor", 1m, 2_850, MondayMorning.AddHours(2)),
                Line(job, LineItemKind.Labor, "Second visit", 1m, 9_500, MondayMorning),
            ]));

        Assert.Equal(2, Saved(slice).Lines.Count);
    }

    /// <summary>
    /// Work finished in a basement at two and pushed at six finished at two, and the invoice and
    /// every <c>JobCompleted</c> handler reads that instant.
    /// </summary>
    [Fact]
    public async Task CompletesWorkWhenTheTechnicianSaysItFinished()
    {
        await using var slice = SliceHost.Sync();
        var job = await AJobInProgress(slice);
        var inTheBasement = MondayMorning.AddHours(5);
        slice.Clock.UtcNow = MondayMorning.AddHours(9);

        await slice.Send(new PushOpsCommand(
            Technician,
            [Status(job, JobStatus.Completed, inTheBasement)]));

        var completed = Assert.IsType<JobCompleted>(Saved(slice).DomainEvents[^1]);
        Assert.Equal(inTheBasement, completed.OccurredAt);
    }

    /// <summary>
    /// The payload may say when the work finished, for a device that recorded the completion later
    /// than it happened. When it does, that instant wins over the operation's own timestamp.
    /// </summary>
    [Fact]
    public async Task PrefersTheCompletionTimeThePayloadStates()
    {
        await using var slice = SliceHost.Sync();
        var job = await AJobInProgress(slice);
        var finished = MondayMorning.AddHours(2);

        await slice.Send(new PushOpsCommand(
            Technician,
            [
                Op(job, FieldOps.StatusChange, $$"""
                    {"status":"Completed","completedAt":"{{finished:O}}"}
                    """, MondayMorning.AddHours(6)),
            ]));

        Assert.Equal(finished, Assert.IsType<JobCompleted>(Saved(slice).DomainEvents[^1]).OccurredAt);
    }

    [Theory]
    [InlineData("job", "delete_job")]
    [InlineData("invoice", "add_line_item")]
    [InlineData("", "")]
    public async Task RefusesAnOperationItCannotApplyWithoutTakingTheBatchDown(string entity, string type)
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);
        var unknown = Op(job, type, """{}""", MondayMorning) with { Entity = entity };
        var good = Note(job, "Still applied.", MondayMorning.AddMinutes(1));

        var pushed = await slice.Send(new PushOpsCommand(Technician, [unknown, good]));

        Assert.True(pushed.IsSuccess);
        Assert.Equal([good.Id], pushed.Value.Applied);
        Assert.Equal(SyncErrors.UnsupportedOperationCode, Assert.Single(pushed.Value.Conflicts).Error.Code);
    }

    /// <summary>
    /// Legal in the exported transition table and driven by nobody in the field: invoicing moves a
    /// job to <see cref="JobStatus.Invoiced"/> through the invoice aggregate. Refused as an
    /// operation this server does not perform rather than as a conflict about the job's state.
    /// </summary>
    [Theory]
    [InlineData(JobStatus.Invoiced)]
    [InlineData(JobStatus.Paid)]
    [InlineData(JobStatus.Unscheduled)]
    public async Task RefusesAStatusNoFieldOperationDrives(JobStatus status)
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);

        var pushed = await slice.Send(new PushOpsCommand(Technician, [Status(job, status, MondayMorning)]));

        Assert.Equal(SyncErrors.MalformedOperationCode, Assert.Single(pushed.Value.Conflicts).Error.Code);
    }

    [Theory]
    // Nothing where the status should be, and a status that is not one.
    [InlineData(FieldOps.StatusChange, """{}""")]
    [InlineData(FieldOps.StatusChange, """{"status":"Napping"}""")]
    [InlineData(FieldOps.StatusChange, """{"status":"EnRoute","completedAt":"whenever"}""")]
    // A note with nothing in it, and one whose text is not text.
    [InlineData(FieldOps.AddNote, """{"text":""}""")]
    [InlineData(FieldOps.AddNote, """{"note":"wrong field"}""")]
    // A line missing each of the four things a line has to say, then two the domain refuses.
    [InlineData(FieldOps.AddLineItem, """{"description":"Filter","quantity":1,"unitPriceCents":100}""")]
    [InlineData(FieldOps.AddLineItem, """{"kind":"Part","quantity":1,"unitPriceCents":100}""")]
    [InlineData(FieldOps.AddLineItem, """{"kind":"Part","description":"Filter","unitPriceCents":100}""")]
    [InlineData(FieldOps.AddLineItem, """{"kind":"Part","description":"Filter","quantity":"1","unitPriceCents":100}""")]
    [InlineData(FieldOps.AddLineItem, """{"kind":"Part","description":"Filter","quantity":1}""")]
    [InlineData(FieldOps.AddLineItem, """{"kind":"Part","description":"Filter","quantity":-2,"unitPriceCents":100}""")]
    [InlineData(FieldOps.AddLineItem, """{"kind":"Part","description":"   ","quantity":1,"unitPriceCents":100}""")]
    public async Task RefusesAnOperationWhosePayloadCannotBeApplied(string type, string payload)
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);

        var pushed = await slice.Send(new PushOpsCommand(Technician, [Op(job, type, payload, MondayMorning)]));

        Assert.True(pushed.IsSuccess);
        Assert.Equal(SyncErrors.MalformedOperationCode, Assert.Single(pushed.Value.Conflicts).Error.Code);
    }

    /// <summary>
    /// A job this tenant does not have, which is also what a job belonging to another organization
    /// looks like from here — the repository is filtered, so "not yours" and "not there" are one
    /// answer by design.
    /// </summary>
    [Fact]
    public async Task RefusesAnOperationForAJobThisTenantDoesNotHave()
    {
        await using var slice = SliceHost.Sync();

        var pushed = await slice.Send(new PushOpsCommand(
            Technician,
            [Status(JobId.New(), JobStatus.EnRoute, MondayMorning)]));

        Assert.Equal(JobErrors.NotFoundCode, Assert.Single(pushed.Value.Conflicts).Error.Code);
    }

    /// <summary>
    /// The op log records applied work and nothing else, which is what makes a re-push idempotent
    /// and a re-sent refusal re-judged rather than remembered.
    /// </summary>
    [Fact]
    public async Task RecordsAppliedOperationsAndNotRefusedOnes()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);
        var applied = Note(job, "In the loft.", MondayMorning);

        await slice.Send(new PushOpsCommand(
            Technician,
            [applied, Status(job, JobStatus.Completed, MondayMorning.AddMinutes(1))]));

        var logged = Assert.Single(slice.Store<SyncOpRecord>().Saved);
        Assert.Equal(applied.Id, logged.Id);
        Assert.Equal(slice.Tenant, logged.OrgId);
        Assert.Equal(Technician, logged.TechnicianId);
        Assert.Equal(job.Value, logged.EntityId);
        Assert.Equal(MondayMorning, logged.ClientTs);
        Assert.Equal(slice.Clock.UtcNow, logged.AppliedAt);
        Assert.Contains("In the loft.", logged.Payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every push hands back where the device now stands, including one that did nothing: a cursor
    /// is how a device finds out what it missed, and it needs one whether or not it had anything
    /// to say.
    /// </summary>
    [Fact]
    public async Task AnswersAnEmptyPushWithACursorAndNothingElse()
    {
        await using var slice = SliceHost.Sync();

        var pushed = await slice.Send(new PushOpsCommand(Technician, []));

        Assert.Empty(pushed.Value.Applied);
        Assert.Empty(pushed.Value.Conflicts);
        Assert.Equal(slice.Fake<SteppingCursors>().Issued, pushed.Value.Cursor);
    }

    [Fact]
    public async Task RefusesABatchWhoseOperationsCannotBeAnsweredAtAll()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);

        var noId = await slice.Send(new PushOpsCommand(
            Technician,
            [Note(job, "Anonymous.", MondayMorning) with { Id = default }]));

        Assert.IsType<ValidationError>(noId.Error);

        var noTechnician = await slice.Send(new PushOpsCommand(default, [Note(job, "Nobody.", MondayMorning)]));

        Assert.IsType<ValidationError>(noTechnician.Error);
    }

    [Fact]
    public async Task RefusesABatchTooBigToBeOneTransaction()
    {
        await using var slice = SliceHost.Sync();
        var job = await ADispatchedJob(slice);
        var ops = Enumerable
            .Range(0, PushOpsCommand.MaxOps + 1)
            .Select(minute => Note(job, "Chatty.", MondayMorning.AddMinutes(minute)))
            .ToArray();

        var pushed = await slice.Send(new PushOpsCommand(Technician, ops));

        Assert.IsType<ValidationError>(pushed.Error);
    }

    private static Job Saved(SliceHost slice) => Assert.Single(slice.Store<Job>().Saved);

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

    private static async Task<JobId> AJobInProgress(SliceHost slice)
    {
        var job = await ADispatchedJob(slice);

        foreach (var status in new[] { JobStatus.EnRoute, JobStatus.InProgress })
        {
            await slice.Send(new ChangeJobStatusCommand(job, status));
        }

        return job;
    }

    /// <summary>
    /// Arranged through the slices that own these aggregates, as a real caller would: a job only
    /// reaches a technician's phone once it has been planned and sent.
    /// </summary>
    private static async Task<JobId> ADispatchedJob(SliceHost slice)
    {
        var customer = await slice.Send(new CreateCustomerCommand("Vance Refrigeration", null, null));
        var location = await slice.Send(new AddServiceLocationCommand(
            customer.Value,
            "Head office",
            "12 Bath Road, Slough",
            51.5107,
            -0.5950));

        var job = await slice.Send(new CreateJobCommand(
            customer.Value,
            location.Value,
            "hvac",
            JobPriority.Normal,
            MondayMorning,
            MondayMorning.AddHours(3),
            TimeSpan.FromHours(1)));

        await slice.Send(new ChangeJobStatusCommand(job.Value, JobStatus.Scheduled));
        await slice.Send(new ChangeJobStatusCommand(job.Value, JobStatus.Dispatched));

        return job.Value;
    }
}
