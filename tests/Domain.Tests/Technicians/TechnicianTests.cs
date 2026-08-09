using OpenDispatch.Domain.Common;
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
}
