using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Domain.Tests.Jobs;

/// <summary>
/// A job that cannot be worked is worse than no job — it sits in the scheduler consuming a
/// slot and never completes. These are the ways one could be booked.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class JobCreationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsAJobWithNoRequiredSkill(string skill)
    {
        Assert.Throws<DomainException>(() => JobBuilder.Any().WithSkill(skill).Build());
    }

    [Fact]
    public void RejectsAJobExpectedToTakeNoTime()
    {
        Assert.Throws<DomainException>(() => JobBuilder.Any().Lasting(TimeSpan.Zero).Build());
    }

    [Fact]
    public void RejectsAJobExpectedToTakeNegativeTime()
    {
        Assert.Throws<DomainException>(() => JobBuilder.Any().Lasting(TimeSpan.FromHours(-1)).Build());
    }

    [Fact]
    public void RejectsAPriorityThatIsNotOneOfTheOnesThatExist()
    {
        // What a JSON body carrying "priority": 99 turns into after model binding.
        Assert.Throws<DomainException>(() => JobBuilder.Any().WithPriority((JobPriority)99).Build());
    }

    [Fact]
    public void TrimsTheRequiredSkillSoWhitespaceNeverBecomesASkillMismatch()
    {
        var job = JobBuilder.Any().WithSkill("  hvac  ").Build();

        Assert.Equal("hvac", job.RequiredSkill);
    }

    [Fact]
    public void ANewJobHasRaisedNothing()
    {
        // Booking demand is not an event anything reacts to; the seam opens once the job
        // starts moving.
        var job = Job.Create(
            OrgId.New(),
            CustomerId.New(),
            ServiceLocationId.New(),
            new GeoPoint(51.5074d, -0.1278d),
            "hvac",
            JobPriority.Normal,
            new TimeWindow(
                new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 10, 12, 0, 0, TimeSpan.Zero)),
            TimeSpan.FromHours(1));

        Assert.Empty(job.DomainEvents);
    }
}
