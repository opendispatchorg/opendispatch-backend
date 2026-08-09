using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Fixtures;

namespace OpenDispatch.Scheduling.Tests.Fixtures;

/// <summary>
/// Pins the shared fixture the rest of Phase 2 is measured against. Every later scheduling
/// test asserts something about what the engine does to this city, so a quiet change here
/// would show up as the engine appearing to regress — and would be looked for in the solver,
/// which is the wrong place.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class SmallCityTests
{
    [Fact]
    public void EveryJobHasSomebodyQualifiedToTakeIt()
    {
        // The baseline city is solvable on purpose. A test about jobs that cannot be placed
        // adds its own rather than making everyone else's expectations wrong.
        var uncoverable = SmallCity.Jobs
            .Where(job => !SmallCity.Technicians.Any(tech => tech.HasSkill(job.RequiredSkill)))
            .Select(job => job.RequiredSkill);

        Assert.Empty(uncoverable);
    }

    [Fact]
    public void EveryShiftFitsInsideTheDayBeingPlanned()
    {
        Assert.All(
            SmallCity.Technicians,
            tech => Assert.True(
                tech.Shift.Start >= SmallCity.Day.Start && tech.Shift.End <= SmallCity.Day.End,
                $"{tech.Id.Value} works outside the horizon."));
    }

    [Fact]
    public void EveryJobCouldInPrincipleBeDoneInsideItsWindow()
    {
        // Not a claim that the engine will place them there — lateness is soft. A window too
        // short to contain its own job would mean the fixture penalises every possible
        // schedule, which would quietly flatten the differences later steps measure.
        Assert.All(
            SmallCity.Jobs,
            job => Assert.True(
                job.Duration <= job.Window.Duration,
                $"{job.RequiredSkill} at {job.Location.Lat} cannot fit in the window it was promised."));
    }

    [Fact]
    public void TheCityIsIdenticalOnEveryRun()
    {
        // The identifiers are hand-written rather than minted, so a schedule can be compared
        // across runs and processes. Step 17's determinism claim rests on this.
        var problem = SmallCity.Problem().Build();
        var again = SmallCity.Problem().Build();

        Assert.Equal(problem.Jobs.Select(j => j.Id), again.Jobs.Select(j => j.Id));
        Assert.Equal(problem.Technicians.Select(t => t.Id), again.Technicians.Select(t => t.Id));
        Assert.Equal("22222222-0000-0000-0000-000000000001", problem.Jobs[0].Id.Value.ToString());
    }

    [Fact]
    public void TheDayHasMoreThanOneTechnicianAndEnoughWorkToChooseBetweenThem()
    {
        // A city that only ever admits one answer cannot show that one schedule is better
        // than another, which is the entire point of the steps that follow.
        var problem = SmallCity.Problem().Build();

        Assert.Equal(3, problem.Technicians.Length);
        Assert.Equal(8, problem.Jobs.Length);
    }
}
