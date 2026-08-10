using OpenDispatch.Application.Customers.AddServiceLocation;
using OpenDispatch.Application.Customers.CreateCustomer;
using OpenDispatch.Application.Jobs;
using OpenDispatch.Application.Jobs.ChangeJobStatus;
using OpenDispatch.Application.Jobs.CreateJob;
using OpenDispatch.Application.Results;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Jobs;

/// <summary>
/// Driving a job through its life, sent through the real pipeline.
/// </summary>
/// <remarks>
/// The state machine itself is exhaustively tested in <c>Domain.Tests</c> and is not restated
/// here. What this slice adds is the translation: a target status becomes the intent method that
/// means it, an illegal move becomes a failure <c>Result</c> rather than an exception at the edge,
/// and a status no request drives is refused before any of that.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class ChangeJobStatusTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task WalksAJobThroughAWholeDayAndLetsTheDomainAnnounceEachStep()
    {
        await using var slice = SliceHost.Jobs();
        var job = await ABookedJob(slice);

        foreach (var status in new[]
        {
            JobStatus.Scheduled,
            JobStatus.Dispatched,
            JobStatus.EnRoute,
            JobStatus.InProgress,
            JobStatus.Completed,
        })
        {
            Assert.True((await slice.Send(new ChangeJobStatusCommand(job, status))).IsSuccess);
        }

        var moved = Assert.Single(slice.Store<Job>().Saved);
        Assert.Equal(JobStatus.Completed, moved.Status);

        // The events are the domain's, raised by the intent methods the handler called — nothing
        // in the slice constructs one, which is what makes step 31's dispatch worth having.
        Assert.Equal(
            [
                typeof(JobScheduled),
                typeof(JobDispatched),
                typeof(JobEnRoute),
                typeof(JobInProgress),
                typeof(JobCompleted),
            ],
            moved.DomainEvents.Select(raised => raised.GetType()));
    }

    /// <summary>
    /// The step's own requirement: a move the state machine forbids comes back as a failure
    /// carrying a conflict, and nothing throws on the way out.
    /// </summary>
    [Fact]
    public async Task RefusesAnIllegalMoveWithAFailureRatherThanAnException()
    {
        await using var slice = SliceHost.Jobs();
        var job = await ABookedJob(slice);

        var result = await slice.Send(new ChangeJobStatusCommand(job, JobStatus.Completed));

        Assert.True(result.IsFailure);
        Assert.Equal(JobErrors.IllegalTransitionCode, result.Error!.Code);
        Assert.Equal(ErrorCategory.Conflict, result.Error.Category);

        // Both ends named, because the caller's whole problem is that it thought the job was
        // somewhere else.
        Assert.Contains("Unscheduled", result.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Completed", result.Error.Message, StringComparison.Ordinal);

        var untouched = Assert.Single(slice.Store<Job>().Saved);
        Assert.Equal(JobStatus.Unscheduled, untouched.Status);
        Assert.Empty(untouched.DomainEvents);
    }

    [Fact]
    public async Task RefusesToMoveAJobThatIsAlreadyCancelled()
    {
        await using var slice = SliceHost.Jobs();
        var job = await ABookedJob(slice);
        await slice.Send(new ChangeJobStatusCommand(job, JobStatus.Cancelled));

        var result = await slice.Send(new ChangeJobStatusCommand(job, JobStatus.Scheduled));

        Assert.Equal(JobErrors.IllegalTransitionCode, result.Error!.Code);
        Assert.Equal(JobStatus.Cancelled, Assert.Single(slice.Store<Job>().Saved).Status);
    }

    /// <summary>
    /// Legal in the exported transition table, but not something any actor does: invoicing drives
    /// these through the invoice aggregate in step 40. Refused as a malformed request rather than
    /// as a conflict, because it is the request that is wrong and not the state of the world.
    /// </summary>
    [Theory]
    [InlineData(JobStatus.Invoiced)]
    [InlineData(JobStatus.Paid)]
    [InlineData(JobStatus.Unscheduled)]
    public async Task RefusesAStatusNoRequestDrives(JobStatus status)
    {
        await using var slice = SliceHost.Jobs();
        var job = await ABookedJob(slice);

        var result = await slice.Send(new ChangeJobStatusCommand(job, status));

        var failure = Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(
            "A job cannot be moved to that status by this request.",
            Assert.Single(failure.Failures[nameof(ChangeJobStatusCommand.Status)]));
    }

    [Fact]
    public async Task RefusesAJobThisTenantDoesNotHave()
    {
        await using var slice = SliceHost.Jobs();

        var result = await slice.Send(new ChangeJobStatusCommand(JobId.New(), JobStatus.Scheduled));

        Assert.Equal(JobErrors.NotFoundCode, result.Error!.Code);
        Assert.Equal(ErrorCategory.NotFound, result.Error.Category);
    }

    /// <summary>
    /// Work finished in a basement at two o'clock and reported at six is work that happened at
    /// two, and the invoice, the day's numbers and every <c>JobCompleted</c> handler read this.
    /// </summary>
    [Fact]
    public async Task RecordsWhenTheWorkActuallyFinishedWhenTheCallerSaysSo()
    {
        await using var slice = SliceHost.Jobs();
        var job = await AJobInProgress(slice);
        var inTheBasement = MondayMorning.AddHours(5);

        await slice.Send(new ChangeJobStatusCommand(job, JobStatus.Completed, inTheBasement));

        var completed = LastEventOn<JobCompleted>(slice);
        Assert.Equal(inTheBasement, completed.OccurredAt);
    }

    [Fact]
    public async Task AsksTheClockWhenNobodySaysWhenTheWorkFinished()
    {
        await using var slice = SliceHost.Jobs();
        var job = await AJobInProgress(slice);
        slice.Clock.UtcNow = MondayMorning.AddHours(8);

        await slice.Send(new ChangeJobStatusCommand(job, JobStatus.Completed));

        Assert.Equal(slice.Clock.UtcNow, LastEventOn<JobCompleted>(slice).OccurredAt);
    }

    private static TEvent LastEventOn<TEvent>(SliceHost slice)
        where TEvent : class =>
        Assert.IsType<TEvent>(Assert.Single(slice.Store<Job>().Saved).DomainEvents[^1]);

    private static async Task<JobId> AJobInProgress(SliceHost slice)
    {
        var job = await ABookedJob(slice);

        foreach (var status in new[]
        {
            JobStatus.Scheduled,
            JobStatus.Dispatched,
            JobStatus.EnRoute,
            JobStatus.InProgress,
        })
        {
            await slice.Send(new ChangeJobStatusCommand(job, status));
        }

        return job;
    }

    /// <summary>Arranged through the slices that own these aggregates, as a real caller would.</summary>
    private static async Task<JobId> ABookedJob(SliceHost slice)
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

        return job.Value;
    }
}
