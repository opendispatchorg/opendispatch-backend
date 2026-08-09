using OpenDispatch.Domain.Jobs;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Scheduling.Tests.Model;

/// <summary>
/// The engine's view of a job. Everything it refuses here is something that would otherwise
/// come out the far end as a schedule rather than as an error.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class SchedJobTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesAJobThatNamesNoSkill(string skill)
    {
        // A blank required skill matches nobody, so the job would be silently unassignable
        // with no explanation anywhere.
        Assert.Throws<ArgumentException>(() => SchedJobBuilder.Any().WithSkill(skill).Build());
    }

    [Fact]
    public void KeepsTheSkillWithoutItsStrayWhitespace()
    {
        var job = SchedJobBuilder.Any().WithSkill("  hvac ").Build();

        Assert.Equal("hvac", job.RequiredSkill);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    public void RefusesWorkThatTakesNoTime(int minutes)
    {
        // A zero-length job would let the search stack any number of them on one technician
        // at no cost at all.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SchedJobBuilder.Any().Lasting(TimeSpan.FromMinutes(minutes)).Build());
    }

    [Fact]
    public void RefusesAPriorityThatDoesNotExist()
    {
        // The unassigned penalty is scaled by this number, so a cast integer arriving from a
        // DTO would quietly reprice dropping the job.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SchedJobBuilder.Any().WithPriority((JobPriority)99).Build());
    }
}
