using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Events;
using OpenDispatch.Domain.Identifiers;
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
    [InlineData(JobStatus.Scheduled, JobStatus.Unscheduled)]   // the optimiser withdrew the plan
    [InlineData(JobStatus.Dispatched, JobStatus.Unscheduled)]  // ...even after the phone was told
    public void LegalTransitionMovesTheJobAndRaisesItsEvent(JobStatus from, JobStatus to)
    {
        var job = JobBuilder.Any().InStatus(from).Build();

        MoveTo(job, to);

        Assert.Equal(to, job.Status);
        var raised = Assert.Single(job.DomainEvents);
        Assert.IsType(EventRaisedOnReaching(to), raised);
        Assert.Equal(job.Id, JobNamedBy(raised));
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
    [InlineData(JobStatus.EnRoute, JobStatus.Unscheduled)]      // the day belongs to the technician
    [InlineData(JobStatus.InProgress, JobStatus.Unscheduled)]   // work under way is not demand
    [InlineData(JobStatus.Completed, JobStatus.Unscheduled)]    // doing it again is a new job
    public void IllegalTransitionThrowsAndLeavesTheJobExactlyAsItWas(JobStatus from, JobStatus to)
    {
        var job = JobBuilder.Any().InStatus(from).Build();

        Assert.Throws<DomainException>(() => MoveTo(job, to));

        Assert.Equal(from, job.Status);
        Assert.Empty(job.DomainEvents);
    }

    /// <summary>
    /// Every status a job can be in, against every status an intent method can move it to.
    /// </summary>
    public static TheoryData<JobStatus, JobStatus> EveryMoveThatCouldBeAsked()
    {
        JobStatus[] from =
        [
            JobStatus.Unscheduled,
            JobStatus.Scheduled,
            JobStatus.Dispatched,
            JobStatus.EnRoute,
            JobStatus.InProgress,
            JobStatus.Completed,
            JobStatus.Invoiced,
            JobStatus.Paid,
            JobStatus.Cancelled,
        ];

        // Every status an intent method can reach, which since the plan can be withdrawn is all
        // nine — a job starts Unscheduled and can also be put back there.
        JobStatus[] to =
        [
            JobStatus.Unscheduled,
            JobStatus.Scheduled,
            JobStatus.Dispatched,
            JobStatus.EnRoute,
            JobStatus.InProgress,
            JobStatus.Completed,
            JobStatus.Invoiced,
            JobStatus.Paid,
            JobStatus.Cancelled,
        ];

        var pairs = new TheoryData<JobStatus, JobStatus>();

        foreach (var start in from)
        {
            foreach (var target in to)
            {
                pairs.Add(start, target);
            }
        }

        return pairs;
    }

    /// <summary>
    /// Asking is the same as trying, for every pair. This is the claim anything that reports an
    /// illegal move rather than crashing on one depends on — step 35's handler returns a failure
    /// <c>Result</c> by asking first, and would be lying if the answer could differ from what
    /// calling the intent method actually does.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryMoveThatCouldBeAsked))]
    public void AskingWhetherAMoveIsLegalGivesTheSameAnswerAsMakingIt(JobStatus from, JobStatus to)
    {
        var job = JobBuilder.Any().InStatus(from).Build();

        var permitted = job.CanTransition(to);

        var moved = true;
        try
        {
            MoveTo(job, to);
        }
        catch (DomainException)
        {
            moved = false;
        }

        Assert.Equal(permitted, moved);
        Assert.Equal(permitted ? to : from, job.Status);
    }

    /// <summary>
    /// The two transitions that announce nothing, and the only ones. A job reaching
    /// <c>Invoiced</c> or <c>Paid</c> is a consequence of something an <c>Invoice</c> did, and
    /// <c>InvoicePaid</c> already carries the job's identity — a second event describing the same
    /// fact from the other side would be two announcements of one thing.
    /// </summary>
    [Theory]
    [InlineData(JobStatus.Completed, JobStatus.Invoiced)]
    [InlineData(JobStatus.Invoiced, JobStatus.Paid)]
    public void BillingAJobMovesItWithoutAnnouncingAnything(JobStatus from, JobStatus to)
    {
        var job = JobBuilder.Any().InStatus(from).Build();

        MoveTo(job, to);

        Assert.Equal(to, job.Status);
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
            case JobStatus.Unscheduled:
                job.Unschedule();
                break;
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
            case JobStatus.Invoiced:
                job.MarkInvoiced();
                break;
            case JobStatus.Paid:
                job.MarkPaid();
                break;
            case JobStatus.Cancelled:
                job.Cancel();
                break;
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(target), target, "No intent method moves a job to this status yet.");
        }
    }

    // Which job the event is about, dug out per type because the catalog is a flat list of
    // records with no shared "job event" interface. Worth asserting: an event of the right
    // type carrying the wrong id sends every handler after the wrong job.
    private static JobId JobNamedBy(IDomainEvent raised) => raised switch
    {
        JobScheduled scheduled => scheduled.JobId,
        JobDispatched dispatched => dispatched.JobId,
        JobEnRoute enRoute => enRoute.JobId,
        JobInProgress inProgress => inProgress.JobId,
        JobCompleted completed => completed.JobId,
        JobCancelled cancelled => cancelled.JobId,
        JobUnscheduled unscheduled => unscheduled.JobId,
        _ => throw new ArgumentOutOfRangeException(
            nameof(raised), raised, "Not an event a job raises."),
    };

    private static Type EventRaisedOnReaching(JobStatus status) => status switch
    {
        JobStatus.Scheduled => typeof(JobScheduled),
        JobStatus.Dispatched => typeof(JobDispatched),
        JobStatus.EnRoute => typeof(JobEnRoute),
        JobStatus.InProgress => typeof(JobInProgress),
        JobStatus.Completed => typeof(JobCompleted),
        JobStatus.Cancelled => typeof(JobCancelled),
        JobStatus.Unscheduled => typeof(JobUnscheduled),
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status, "No intent method moves a job to this status yet."),
    };
}
