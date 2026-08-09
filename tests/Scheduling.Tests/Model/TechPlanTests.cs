using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Technicians;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Scheduling.Tests.Model;

/// <summary>
/// Skill matching is a hard constraint, and this is the second of the three places the
/// case-insensitive comparer has to survive a copy. Losing it here does not throw — it
/// produces a job nobody can be assigned to and no explanation of why, which is the exact
/// failure the soft-lateness design exists to avoid everywhere else.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class TechPlanTests
{
    [Theory]
    [InlineData("hvac")]
    [InlineData("HVAC")]
    [InlineData("Hvac")]
    [InlineData("  hvac  ")]
    public void MatchesASkillHoweverItWasTyped(string asked)
    {
        var plan = TechPlanBuilder.Any().Skilled("HVAC").Build();

        Assert.True(plan.HasSkill(asked));
    }

    [Fact]
    public void TheExposedSkillSetLooksUpWithoutRegardToCaseToo()
    {
        // The greedy constructor may read this set directly rather than calling HasSkill per
        // job. HasSkill reads the private field, so it would keep passing even if this
        // property started handing back a set that had lost the comparer.
        var plan = TechPlanBuilder.Any().Skilled("hvac").Build();

        Assert.Contains("HVAC", plan.Skills);
    }

    [Fact]
    public void CarriesTheComparerAcrossFromTheTechnicianItWasBuiltFrom()
    {
        // The crossing the domain layer was written to survive: a technician's skills are
        // handed to the engine as a plain sequence, and the comparer does not travel with it.
        // TechPlan rebuilding the set is what makes that safe regardless of what the mapper
        // that arrives in step 37 does.
        var technician = Technician.Create(
            OrgId.New(),
            "Sam Rivera",
            ["HVAC"],
            new TimeWindow(TechPlanBuilder.ShiftStart, TechPlanBuilder.ShiftStart.AddHours(9)),
            new GeoPoint(51.5074d, -0.1278d));

        var plan = new TechPlan(technician.Id, technician.Skills, technician.Shift, new GeoPoint(0d, 0d));

        Assert.True(plan.HasSkill("hvac"));
    }

    [Fact]
    public void ATraineeWithNoSkillsMatchesNothing()
    {
        var plan = TechPlanBuilder.Any().Skilled().Build();

        Assert.Empty(plan.Skills);
        Assert.False(plan.HasSkill("hvac"));
    }

    [Fact]
    public void IsNotAffectedByLaterChangesToTheCollectionItWasGiven()
    {
        var skills = new List<string> { "hvac" };

        var plan = new TechPlan(
            TechnicianId.New(),
            skills,
            new TimeWindow(TechPlanBuilder.ShiftStart, TechPlanBuilder.ShiftStart.AddHours(9)),
            new GeoPoint(51.5074d, -0.1278d));
        skills.Add("electrical");

        Assert.False(plan.HasSkill("electrical"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RefusesABlankSkill(string skill)
    {
        Assert.Throws<ArgumentException>(() => TechPlanBuilder.Any().Skilled("hvac", skill).Build());
    }
}
