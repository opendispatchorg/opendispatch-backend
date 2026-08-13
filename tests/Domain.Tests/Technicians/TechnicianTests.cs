using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.TestSupport;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.Domain.Tests.Technicians;

/// <summary>
/// Skill matching is a hard scheduling constraint, so getting it wrong does not throw — it
/// produces a job nobody can be assigned to and no explanation of why. Most of what is
/// here is about the ways two people typing the same skill can fail to agree.
/// </summary>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class TechnicianTests
{
    [Theory]
    [InlineData("hvac")]
    [InlineData("HVAC")]
    [InlineData("Hvac")]
    [InlineData("  hvac  ")]
    public void HasSkillIgnoresHowTheSkillWasTyped(string asked)
    {
        var technician = TechnicianBuilder.Any().Skilled("hvac").Build();

        Assert.True(technician.HasSkill(asked));
    }

    [Fact]
    public void TheExposedSkillSetLooksUpWithoutRegardToCaseToo()
    {
        // The scheduler copies this set straight into its own model rather than calling
        // HasSkill per job. HasSkill reads the private field, so it would keep passing even
        // if this property started handing back a set that had lost the comparer — and the
        // only symptom would be a job nobody can be assigned to.
        var technician = TechnicianBuilder.Any().Skilled("hvac").Build();

        Assert.True(technician.Skills.Contains("HVAC"));
    }

    [Fact]
    public void HasSkillIsFalseForWorkTheyAreNotQualifiedFor()
    {
        var technician = TechnicianBuilder.Any().Skilled("hvac").Build();

        Assert.False(technician.HasSkill("electrical"));
    }

    [Fact]
    public void ATraineeWithNoSkillsMatchesNothing()
    {
        var technician = TechnicianBuilder.Any().Skilled().Build();

        Assert.Empty(technician.Skills);
        Assert.False(technician.HasSkill("hvac"));
    }

    [Fact]
    public void AddingASkillTheyAlreadyHaveChangesNothing()
    {
        var technician = TechnicianBuilder.Any().Skilled("hvac").Build();

        technician.AddSkill("hvac");

        Assert.Single(technician.Skills);
    }

    [Fact]
    public void AddingTheSameSkillInDifferentCaseDoesNotCreateASecondOne()
    {
        var technician = TechnicianBuilder.Any().Skilled("hvac").Build();

        technician.AddSkill("HVAC");

        Assert.Single(technician.Skills);
    }

    [Fact]
    public void AddingASkillWithStrayWhitespaceDoesNotCreateASecondOne()
    {
        var technician = TechnicianBuilder.Any().Skilled("hvac").Build();

        technician.AddSkill("  hvac ");

        Assert.Single(technician.Skills);
    }

    [Fact]
    public void RemovingASkillTakesItAway()
    {
        var technician = TechnicianBuilder.Any().Skilled("hvac", "electrical").Build();

        technician.RemoveSkill("hvac");

        Assert.False(technician.HasSkill("hvac"));
        Assert.True(technician.HasSkill("electrical"));
    }

    [Fact]
    public void RemovingASkillRegardlessOfHowItIsTyped()
    {
        var technician = TechnicianBuilder.Any().Skilled("hvac").Build();

        technician.RemoveSkill("HVAC");

        Assert.Empty(technician.Skills);
    }

    [Fact]
    public void RemovingASkillTheyDoNotHaveDoesNothing()
    {
        // A replayed or re-sent update has to be harmless.
        var technician = TechnicianBuilder.Any().Skilled("hvac").Build();

        technician.RemoveSkill("plumbing");
        technician.RemoveSkill("plumbing");

        Assert.Single(technician.Skills);
    }

    [Fact]
    public void SettingTheSkillsReplacesEveryOneOfThem()
    {
        var technician = TechnicianBuilder.Any().Skilled("hvac", "electrical").Build();

        technician.SetSkills(["plumbing", "gas"]);

        Assert.Equal(["gas", "plumbing"], technician.Skills.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SettingTheSameSkillsInDifferentCaseLeavesOneOfEach()
    {
        var technician = TechnicianBuilder.Any().Skilled("hvac").Build();

        // The reason this method is on the aggregate: a caller diffing "what they have" against
        // "what they should have" with an ordinary comparer would see two skills here.
        technician.SetSkills(["HVAC", "hvac", " Hvac "]);

        Assert.Single(technician.Skills);
        Assert.True(technician.HasSkill("hvac"));
    }

    [Fact]
    public void SettingNoSkillsMakesThemATraineeAgain()
    {
        var technician = TechnicianBuilder.Any().Skilled("hvac", "electrical").Build();

        technician.SetSkills([]);

        Assert.Empty(technician.Skills);
    }

    /// <summary>
    /// All or nothing. A half-applied update would leave a technician qualified for some of what
    /// was asked and not the rest, with nothing to say which — and the scheduler would place work
    /// on that.
    /// </summary>
    [Fact]
    public void ABlankSkillPartWayDownTheListLeavesThemUntouched()
    {
        var technician = TechnicianBuilder.Any().Skilled("hvac").Build();

        Assert.Throws<DomainException>(() => technician.SetSkills(["plumbing", "  ", "gas"]));

        Assert.Equal(["hvac"], technician.Skills);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsABlankSkill(string skill)
    {
        var technician = TechnicianBuilder.Any().Build();

        Assert.Throws<DomainException>(() => technician.AddSkill(skill));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsATechnicianWithNoName(string name)
    {
        Assert.Throws<DomainException>(() => TechnicianBuilder.Any().Named(name).Build());
    }

    [Fact]
    public void RejectsABlankSkillAtCreation()
    {
        Assert.Throws<DomainException>(() => TechnicianBuilder.Any().Skilled("hvac", "  ").Build());
    }

    [Fact]
    public void RenamingTrimsTheNewName()
    {
        var technician = TechnicianBuilder.Any().Build();

        technician.Rename("  Alice Vance  ");

        Assert.Equal("Alice Vance", technician.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void RenamingToNothingIsRefused(string name)
    {
        var technician = TechnicianBuilder.Any().Build();

        Assert.Throws<DomainException>(() => technician.Rename(name));
    }

    [Fact]
    public void MovingTheHomeBaseReplacesIt()
    {
        var technician = TechnicianBuilder.Any().BasedAt(new GeoPoint(51.5074d, -0.1278d)).Build();
        var moved = new GeoPoint(51.51d, -0.12d);

        technician.SetHomeBase(moved);

        Assert.Equal(moved, technician.HomeBase);
    }
}
