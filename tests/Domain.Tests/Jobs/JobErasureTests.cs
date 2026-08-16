using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Invoices;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Domain.Tests.Jobs;

/// <summary>
/// A job's half of an erasure: it carries personal data of its own, and it is the half that has to
/// keep the money.
/// </summary>
/// <remarks>
/// The job is where erasure is easiest to get wrong in both directions — deleting it would take a
/// shop's financial record with it, and leaving it alone would leave the coordinates of somebody's
/// house and whatever a technician wrote about being inside it.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class JobErasureTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ErasingTakesTheCoordinatesAndTheNotes()
    {
        var job = JobBuilder.Any().At(new GeoPoint(51.5074d, -0.1278d)).Build();
        job.RecordNotes("Key under the mat, dog in the kitchen.", Noon);

        job.Erase(Noon.AddDays(30));

        Assert.Equal(Tombstone.Point, job.Location);
        Assert.Null(job.Notes);
        Assert.Null(job.NotesRecordedAt);
        Assert.True(job.IsErased);
    }

    /// <summary>
    /// What a shop is required to keep, and what an erasure request does not reach: the hours, the
    /// parts and their prices — the record the invoice was raised from.
    /// </summary>
    [Fact]
    public void ErasingKeepsWhatTheWorkCost()
    {
        var job = JobBuilder.Any().InStatus(JobStatus.Completed).Build();
        job.RecordLine(LineItemKind.Labor, "Two hours", 2m, Money.FromDollars(65m), Noon);
        job.RecordLine(LineItemKind.Part, "Thermostat", 1m, Money.FromDollars(120m), Noon);

        job.Erase(Noon.AddDays(30));

        Assert.Equal(2, job.Lines.Count);
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Equal(Money.FromDollars(250m), job.Lines.Aggregate(Money.Zero, (total, line) => total.Add(line.LineTotal)));
    }

    /// <summary>
    /// The case that makes this an invariant rather than a scrub: a phone that was out of signal
    /// when the erasure ran still holds the job, and pushes what somebody wrote about the visit
    /// afterwards. Taking it would put the personal data straight back.
    /// </summary>
    [Fact]
    public void NotesArrivingAfterAnErasureAreRefused()
    {
        var job = JobBuilder.Any().Build();
        job.Erase(Noon);

        Assert.Throws<DomainException>(() => job.RecordNotes("Meter behind the boiler.", Noon.AddHours(1)));
        Assert.Null(job.Notes);
    }

    [Fact]
    public void ErasingAgainChangesNothing()
    {
        var job = JobBuilder.Any().Build();

        job.Erase(Noon);
        job.Erase(Noon.AddDays(30));

        Assert.Equal(Noon, job.ErasedAt);
    }
}
