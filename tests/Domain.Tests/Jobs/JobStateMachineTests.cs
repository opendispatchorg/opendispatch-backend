using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Domain.Tests.Jobs;

/// <summary>
/// The state machine is the one rule in the system that every other layer trusts blindly —
/// the sync endpoint validates a stale phone's status change against it, and the dispatch
/// board assumes what it is shown is reachable. So every move it permits is exercised, and
/// the ways it is likely to be pushed sideways are exercised too.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class JobStateMachineTests
{
    private static readonly DateTimeOffset CompletedAt = new(2026, 8, 10, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public void CreateYieldsAnUnscheduledJob()
    {
        var job = JobBuilder.Any().Build();

        Assert.Equal(JobStatus.Unscheduled, job.Status);
    }

    [Theory]
    [InlineData(JobStatus.Unscheduled, JobStatus.Scheduled)]
    [InlineData(JobStatus.Unscheduled, JobStatus.Cancelled)]
    [InlineData(JobStatus.Scheduled, JobStatus.Dispatched)]
    [InlineData(JobStatus.Scheduled, JobStatus.Cancelled)]
    [InlineData(JobStatus.Dispatched, JobStatus.EnRoute)]
    [InlineData(JobStatus.Dispatched, JobStatus.Cancelled)]
    [InlineData(JobStatus.EnRoute, JobStatus.InProgress)]
    [InlineData(JobStatus.EnRoute, JobStatus.Cancelled)]
    [InlineData(JobStatus.InProgress, JobStatus.Completed)]
    [InlineData(JobStatus.InProgress, JobStatus.Cancelled)]
    public void LegalTransitionMovesTheJobAndRaisesItsEvent(JobStatus from, JobStatus to)
    {
        var job = JobBuilder.Any().InStatus(from).Build();

        MoveTo(job, to);

        Assert.Equal(to, job.Status);
        var raised = Assert.Single(job.DomainEvents);
        Assert.IsType(EventRaisedOnReaching(to), raised);
    }

    [Theory]
    [InlineData(JobStatus.Unscheduled, JobStatus.Dispatched)]  // nobody is going to it yet
    [InlineData(JobStatus.Unscheduled, JobStatus.Completed)]   // work that never happened
    [InlineData(JobStatus.Scheduled, JobStatus.EnRoute)]       // the phone has not been told
    [InlineData(JobStatus.Dispatched, JobStatus.InProgress)]   // skipping the drive
    [InlineData(JobStatus.InProgress, JobStatus.Dispatched)]   // backwards
    [InlineData(JobStatus.Completed, JobStatus.Cancelled)]     // the work happened; this is a credit note
    [InlineData(JobStatus.Completed, JobStatus.Scheduled)]     // doing it again is a new job
    [InlineData(JobStatus.Cancelled, JobStatus.Scheduled)]     // terminal
    [InlineData(JobStatus.Cancelled, JobStatus.Cancelled)]     // cancelling twice
    public void IllegalTransitionThrowsAndLeavesTheJobExactlyAsItWas(JobStatus from, JobStatus to)
    {
        var job = JobBuilder.Any().InStatus(from).Build();

        Assert.Throws<DomainException>(() => MoveTo(job, to));

        Assert.Equal(from, job.Status);
        Assert.Empty(job.DomainEvents);
    }

    [Fact]
    public void CompletionRecordsWhenTheWorkFinishedNotWhenItWasReported()
    {
        var job = JobBuilder.Any().InStatus(JobStatus.InProgress).Build();

        job.MarkCompleted(CompletedAt);

        var completed = Assert.IsType<JobCompleted>(Assert.Single(job.DomainEvents));
        Assert.Equal(CompletedAt, completed.OccurredAt);
    }

    [Fact]
    public void EventsAccumulateAcrossAWholeDayOfWork()
    {
        var job = JobBuilder.Any().Build();

        job.Schedule();
        job.Dispatch();
        job.MarkEnRoute();
        job.MarkInProgress();
        job.MarkCompleted(CompletedAt);

        Assert.Collection(
            job.DomainEvents,
            e => Assert.IsType<JobScheduled>(e),
            e => Assert.IsType<JobDispatched>(e),
            e => Assert.IsType<JobEnRoute>(e),
            e => Assert.IsType<JobInProgress>(e),
            e => Assert.IsType<JobCompleted>(e));
    }

    private static void MoveTo(Job job, JobStatus target)
    {
        switch (target)
        {
            case JobStatus.Scheduled:
                job.Schedule();
                break;
            case JobStatus.Dispatched:
                job.Dispatch();
                break;
            case JobStatus.EnRoute:
                job.MarkEnRoute();
                break;
            case JobStatus.InProgress:
                job.MarkInProgress();
                break;
            case JobStatus.Completed:
                job.MarkCompleted(CompletedAt);
                break;
            case JobStatus.Cancelled:
                job.Cancel();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(target), target, "No intent method moves a job to this status yet.");
        }
    }

    private static Type EventRaisedOnReaching(JobStatus status) => status switch
    {
        JobStatus.Scheduled => typeof(JobScheduled),
        JobStatus.Dispatched => typeof(JobDispatched),
        JobStatus.EnRoute => typeof(JobEnRoute),
        JobStatus.InProgress => typeof(JobInProgress),
        JobStatus.Completed => typeof(JobCompleted),
        JobStatus.Cancelled => typeof(JobCancelled),
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status, "No intent method moves a job to this status yet."),
    };
}
