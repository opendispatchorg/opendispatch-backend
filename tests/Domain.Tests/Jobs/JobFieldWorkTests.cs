using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Domain.Tests.Jobs;

/// <summary>
/// What a technician records on a job: what they found, and what the work took.
/// </summary>
/// <remarks>
/// The last-write-wins rule on notes is the one worth the depth. It is the domain's half of
/// Document 2 §10's conflict policy, it is asked by the sync push before it is applied, and it is
/// decided by the writer's clock rather than by which push arrived first — which is the whole
/// point, because the writer with the oldest clock is usually the one that has been offline.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class JobFieldWorkTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void HasNothingRecordedOnIt()
    {
        var job = JobBuilder.Any().Build();

        Assert.Null(job.Notes);
        Assert.Null(job.NotesRecordedAt);
        Assert.Empty(job.Lines);
    }

    [Fact]
    public void KeepsTheFirstNotesAnybodyWrites()
    {
        var job = JobBuilder.Any().Build();

        job.RecordNotes("  Meter behind the boiler.  ", Noon);

        Assert.Equal("Meter behind the boiler.", job.Notes);
        Assert.Equal(Noon, job.NotesRecordedAt);
    }

    [Fact]
    public void ReplacesNotesWithSomethingWrittenLater()
    {
        var job = JobBuilder.Any().Build();
        job.RecordNotes("Nobody home.", Noon);

        job.RecordNotes("Came back, let in by the neighbour.", Noon.AddHours(2));

        Assert.Equal("Came back, let in by the neighbour.", job.Notes);
        Assert.Equal(Noon.AddHours(2), job.NotesRecordedAt);
    }

    /// <summary>
    /// The case the rule exists for: a phone that has been in a basement since nine pushes at six,
    /// and what it wrote at nine does not overwrite what the office wrote at noon.
    /// </summary>
    [Fact]
    public void RefusesNotesWrittenBeforeTheOnesItHolds()
    {
        var job = JobBuilder.Any().Build();
        job.RecordNotes("Office: customer rang to say the boiler is out.", Noon);

        Assert.False(job.CanRecordNotes(Noon.AddHours(-3)));
        Assert.Throws<DomainException>(() => job.RecordNotes("Nobody home.", Noon.AddHours(-3)));
        Assert.Equal("Office: customer rang to say the boiler is out.", job.Notes);
    }

    /// <summary>
    /// Two writers claiming the same instant have to be separated by something, and "the one
    /// already recorded" is the only tie-break that gives the same answer whichever order they
    /// arrive in.
    /// </summary>
    [Fact]
    public void RefusesNotesClaimingTheSameInstantAsTheOnesItHolds()
    {
        var job = JobBuilder.Any().Build();
        job.RecordNotes("First.", Noon);

        Assert.False(job.CanRecordNotes(Noon));
        Assert.Equal("First.", job.Notes);
    }

    [Fact]
    public void AcceptsAnyNotesWhenItHasNone()
    {
        var job = JobBuilder.Any().Build();

        Assert.True(job.CanRecordNotes(DateTimeOffset.MinValue));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesNotesThatSayNothing(string text)
    {
        var job = JobBuilder.Any().Build();

        Assert.Throws<DomainException>(() => job.RecordNotes(text, Noon));
    }

    /// <summary>
    /// Lines append rather than replace, which is why nothing about them is last-write-wins: two
    /// people recording two parts recorded two parts.
    /// </summary>
    [Fact]
    public void KeepsEveryLineInTheOrderItWasRecorded()
    {
        var job = JobBuilder.Any().Build();

        job.RecordLine(LineItemKind.Labor, "Diagnostic", 1.5m, Money.FromDollars(95m), Noon);
        job.RecordLine(LineItemKind.Part, "Run capacitor", 2m, Money.FromDollars(28.50m), Noon.AddHours(-1));

        Assert.Equal(["Diagnostic", "Run capacitor"], job.Lines.Select(line => line.Description));
        Assert.Equal(14_250L, job.Lines[0].LineTotal.Cents);
        Assert.Equal(5_700L, job.Lines[1].LineTotal.Cents);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("Filter", 0)]
    [InlineData("Filter", -2)]
    public void RefusesALineThatRecordsNothing(string description, decimal quantity)
    {
        var job = JobBuilder.Any().Build();

        Assert.Throws<DomainException>(() =>
            job.RecordLine(LineItemKind.Part, description, quantity, Money.FromDollars(10m), Noon));

        Assert.Empty(job.Lines);
    }

    [Fact]
    public void RefusesALineThatIsNeitherLabourNorAPart()
    {
        var job = JobBuilder.Any().Build();

        Assert.Throws<DomainException>(() =>
            job.RecordLine((LineItemKind)7, "Something", 1m, Money.FromDollars(10m), Noon));
    }

    /// <summary>
    /// Neither raises anything. A note and a line are observations about work that is already
    /// happening rather than steps in the job's life, and the step-13 catalog names no event for
    /// either — so nothing downstream can come to depend on one it will not get.
    /// </summary>
    [Fact]
    public void AnnouncesNothingWhenTheFieldRecordsSomething()
    {
        var job = JobBuilder.Any().Build();

        job.RecordNotes("Quiet.", Noon);
        job.RecordLine(LineItemKind.Part, "Filter", 1m, Money.FromDollars(12m), Noon);

        Assert.Empty(job.DomainEvents);
    }

    /// <summary>
    /// A technician who drove out to a job the office called off an hour ago has still spent the
    /// hour, and refusing the record would lose the only evidence of it. Field work is an
    /// observation, so no status forbids it.
    /// </summary>
    [Theory]
    [InlineData(JobStatus.Cancelled)]
    [InlineData(JobStatus.Completed)]
    public void RecordsFieldWorkWhateverTheJobHasBecome(JobStatus status)
    {
        var job = JobBuilder.Any().Build();
        DriveTo(job, status);

        job.RecordNotes("Drove out; job was called off while I was on the way.", Noon);
        job.RecordLine(LineItemKind.Labor, "Callout", 1m, Money.FromDollars(80m), Noon);

        Assert.NotNull(job.Notes);
        Assert.Single(job.Lines);
    }

    private static void DriveTo(Job job, JobStatus status)
    {
        if (status is JobStatus.Cancelled)
        {
            job.Cancel();

            return;
        }

        job.Schedule();
        job.Dispatch();
        job.MarkEnRoute();
        job.MarkInProgress();
        job.MarkCompleted(Noon);
    }
}
