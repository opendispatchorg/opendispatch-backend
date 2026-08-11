using OpenDispatch.Application.Results;
using OpenDispatch.Application.Technicians;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Application.Technicians.ListTechnicians;
using OpenDispatch.Application.Technicians.SetShift;
using OpenDispatch.Application.Technicians.SetSkills;
using OpenDispatch.Application.Tests.Fakes;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Technicians;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Application.Tests.Technicians;

/// <summary>
/// The four requests of the Technicians slice, sent through the real pipeline.
/// </summary>
/// <remarks>
/// The crew is the resource side of scheduling, so what is worth testing here is what the
/// scheduler will later depend on: that skills survive being replaced without losing their
/// case-insensitive identity, and that neither skills nor a shift can be set on somebody this
/// tenant does not have. That a shift arriving in another offset is stored as the same instant is
/// a claim about a column, and lives with the database in <c>Api.IntegrationTests</c>.
/// </remarks>
[Trait(TestCategories.Name, TestCategories.Unit)]
public sealed class TechniciansSliceTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TakesOnATechnicianThenChangesTheirSkillsAndTheirShift()
    {
        await using var slice = SliceHost.Technicians();

        var created = await slice.Send(NewTechnician("Sam Rivera", ["hvac"]));
        Assert.True(created.IsSuccess);

        Assert.True((await slice.Send(new SetSkillsCommand(created.Value, ["gas", "plumbing"]))).IsSuccess);
        Assert.True((await slice.Send(new SetShiftCommand(
            created.Value,
            MondayMorning.AddHours(2),
            MondayMorning.AddHours(12)))).IsSuccess);

        var crew = await slice.Send(new ListTechniciansQuery());

        var technician = Assert.Single(crew.Value);
        Assert.Equal(created.Value, technician.Id);
        Assert.Equal("Sam Rivera", technician.Name);
        Assert.Equal(["gas", "plumbing"], technician.Skills);
        Assert.Equal(MondayMorning.AddHours(2), technician.ShiftStart);
        Assert.Equal(MondayMorning.AddHours(12), technician.ShiftEnd);
        Assert.Equal(51.5074, technician.Latitude);
        Assert.Equal(-0.1278, technician.Longitude);
    }

    [Fact]
    public async Task ATraineeWithNoSkillsIsATechnician()
    {
        await using var slice = SliceHost.Technicians();

        var created = await slice.Send(NewTechnician("Jo Bennett", []));

        Assert.True(created.IsSuccess);
        Assert.Empty(Assert.Single((await slice.Send(new ListTechniciansQuery())).Value).Skills);
    }

    /// <summary>
    /// Skill identity is the aggregate's rule, and the command states the whole set rather than a
    /// change to it — so re-sending the same skill in different case leaves one skill, not two.
    /// </summary>
    /// <remarks>
    /// The casing that survives is the one most recently typed, which follows from replacing the
    /// set rather than merging into it. That is the right way round: the domain keeps the original
    /// casing so a skill displays the way somebody wrote it, and the person writing it here is the
    /// one doing the update.
    /// </remarks>
    [Fact]
    public async Task ReplacingSkillsWithTheSameOnesTypedDifferentlyLeavesOneOfEach()
    {
        await using var slice = SliceHost.Technicians();
        var created = await slice.Send(NewTechnician("Sam Rivera", ["hvac"]));

        await slice.Send(new SetSkillsCommand(created.Value, ["HVAC", " hvac "]));

        var technician = Assert.Single(slice.Store<Technician>().Saved);
        Assert.Equal(["HVAC"], technician.Skills);
        Assert.True(technician.HasSkill("hvac"));
    }

    [Fact]
    public async Task ClearingTheSkillsMakesThemATraineeAgain()
    {
        await using var slice = SliceHost.Technicians();
        var created = await slice.Send(NewTechnician("Sam Rivera", ["hvac", "gas"]));

        Assert.True((await slice.Send(new SetSkillsCommand(created.Value, []))).IsSuccess);

        Assert.Empty(Assert.Single((await slice.Send(new ListTechniciansQuery())).Value).Skills);
    }

    [Fact]
    public async Task FilesTheTechnicianUnderTheTenantThatTookThemOn()
    {
        await using var slice = SliceHost.Technicians();

        await slice.Send(NewTechnician("Sam Rivera", ["hvac"]));

        Assert.Equal(slice.Tenant, Assert.Single(slice.Store<Technician>().Saved).OrgId);
    }

    [Fact]
    public async Task ListsTheCrewByNameEvenThoughThePortOrdersItForTheScheduler()
    {
        await using var slice = SliceHost.Technicians();

        await slice.Send(NewTechnician("Sam Rivera", ["hvac"]));
        await slice.Send(NewTechnician("Alice Chen", ["gas"]));
        await slice.Send(NewTechnician("jo Bennett", []));

        var crew = await slice.Send(new ListTechniciansQuery());

        Assert.Equal(
            ["Alice Chen", "jo Bennett", "Sam Rivera"],
            crew.Value.Select(technician => technician.Name));
    }

    [Fact]
    public async Task SettingSkillsOnATechnicianThisTenantDoesNotHaveIsAMiss()
    {
        await using var slice = SliceHost.Technicians();

        var result = await slice.Send(new SetSkillsCommand(TechnicianId.New(), ["hvac"]));

        Assert.True(result.IsFailure);
        Assert.Equal(TechnicianErrors.NotFoundCode, result.Error!.Code);
        Assert.Equal(ErrorCategory.NotFound, result.Error.Category);
    }

    [Fact]
    public async Task SettingAShiftOnATechnicianThisTenantDoesNotHaveIsAMiss()
    {
        await using var slice = SliceHost.Technicians();

        var result = await slice.Send(
            new SetShiftCommand(TechnicianId.New(), MondayMorning, MondayMorning.AddHours(9)));

        Assert.True(result.IsFailure);
        Assert.Equal(TechnicianErrors.NotFoundCode, result.Error!.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RefusesATechnicianWithoutAName(string name)
    {
        await using var slice = SliceHost.Technicians();

        var created = await slice.Send(NewTechnician(name, ["hvac"]));

        var failure = Assert.IsType<ValidationError>(created.Error);
        Assert.Equal(
            "A technician must have a name.",
            Assert.Single(failure.Failures[nameof(CreateTechnicianCommand.Name)]));
        Assert.Empty(slice.Store<Technician>().Saved);
    }

    /// <summary>
    /// A blank skill throws in the domain — <c>NormalizeSkill</c> refuses it — so without a rule
    /// here the caller would get an exception instead of being told which entry was wrong.
    /// </summary>
    [Fact]
    public async Task RefusesASkillThatIsNotNamed()
    {
        await using var slice = SliceHost.Technicians();

        var created = await slice.Send(NewTechnician("Sam Rivera", ["hvac", "  "]));

        var failure = Assert.IsType<ValidationError>(created.Error);
        Assert.Equal("Skills[1]", Assert.Single(failure.Failures.Keys));
        Assert.Empty(slice.Store<Technician>().Saved);
    }

    [Fact]
    public async Task RefusesABlankSkillOnAnUpdateToo()
    {
        await using var slice = SliceHost.Technicians();
        var created = await slice.Send(NewTechnician("Sam Rivera", ["hvac"]));

        var result = await slice.Send(new SetSkillsCommand(created.Value, ["gas", ""]));

        Assert.IsType<ValidationError>(result.Error);

        // Refused before the handler, so the aggregate never saw a half-valid list.
        Assert.Equal(["hvac"], Assert.Single(slice.Store<Technician>().Saved).Skills);
    }

    /// <summary>
    /// An inverted window throws in <c>TimeWindow</c>'s constructor, which is what this rule is
    /// standing in front of — the same argument as the coordinate rules in the Customers slice.
    /// </summary>
    [Fact]
    public async Task RefusesAShiftThatEndsBeforeItStarts()
    {
        await using var slice = SliceHost.Technicians();

        var created = await slice.Send(new CreateTechnicianCommand(
            "Sam Rivera",
            ["hvac"],
            MondayMorning,
            MondayMorning.AddHours(-1),
            51.5074,
            -0.1278));

        var failure = Assert.IsType<ValidationError>(created.Error);
        Assert.Equal(
            "A shift cannot end before it starts.",
            Assert.Single(failure.Failures[nameof(CreateTechnicianCommand.ShiftEnd)]));
    }

    [Fact]
    public async Task RefusesAnInvertedShiftOnAnUpdateToo()
    {
        await using var slice = SliceHost.Technicians();
        var created = await slice.Send(NewTechnician("Sam Rivera", ["hvac"]));

        var result = await slice.Send(
            new SetShiftCommand(created.Value, MondayMorning, MondayMorning.AddSeconds(-1)));

        Assert.IsType<ValidationError>(result.Error);
        Assert.Equal(
            MondayMorning.AddHours(9),
            Assert.Single(slice.Store<Technician>().Saved).Shift.End);
    }

    /// <summary>A day off is a zero-length shift, which the domain allows and this must not refuse.</summary>
    [Fact]
    public async Task AcceptsAZeroLengthShiftAsADayOff()
    {
        await using var slice = SliceHost.Technicians();
        var created = await slice.Send(NewTechnician("Sam Rivera", ["hvac"]));

        var result = await slice.Send(new SetShiftCommand(created.Value, MondayMorning, MondayMorning));

        Assert.True(result.IsSuccess);
        Assert.Equal(TimeSpan.Zero, Assert.Single(slice.Store<Technician>().Saved).Shift.Duration);
    }

    [Theory]
    [InlineData(91d, 0d, nameof(CreateTechnicianCommand.Latitude))]
    [InlineData(0d, double.NaN, nameof(CreateTechnicianCommand.Longitude))]
    public async Task RefusesAHomeBaseThatIsNotAPlace(double latitude, double longitude, string field)
    {
        await using var slice = SliceHost.Technicians();

        var created = await slice.Send(new CreateTechnicianCommand(
            "Sam Rivera",
            ["hvac"],
            MondayMorning,
            MondayMorning.AddHours(9),
            latitude,
            longitude));

        var failure = Assert.IsType<ValidationError>(created.Error);
        Assert.Equal(field, Assert.Single(failure.Failures.Keys));
        Assert.Empty(slice.Store<Technician>().Saved);
    }

    private static CreateTechnicianCommand NewTechnician(string name, string[] skills) =>
        new(name, skills, MondayMorning, MondayMorning.AddHours(9), 51.5074, -0.1278);
}
