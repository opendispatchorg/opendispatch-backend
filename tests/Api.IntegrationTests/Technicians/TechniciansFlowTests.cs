using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OpenDispatch.Api.IntegrationTests.Fixtures;
using OpenDispatch.Application.Technicians.CreateTechnician;
using OpenDispatch.Application.Technicians.ListTechnicians;
using OpenDispatch.Application.Technicians.SetShift;
using OpenDispatch.Application.Technicians.SetSkills;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.TestSupport;

namespace OpenDispatch.Api.IntegrationTests.Technicians;

/// <summary>
/// The Technicians slice against a real database, through the registrations the host uses.
/// </summary>
/// <remarks>
/// Two things here can only fail against Postgres. Replacing a skill set mutates a collection in
/// place, so whether the change is noticed at all depends on the value comparer and snapshot in
/// <c>SkillSetMapping</c> — get that wrong and the update is a silent no-op that every fake would
/// let through. And a shift arriving in a non-UTC offset is refused outright by Npgsql against a
/// <c>timestamptz</c> column, which is why the handler converts.
/// </remarks>
[Collection(PostgresCollectionDefinition.Name)]
[Trait(TestCategories.Name, TestCategories.Integration)]
public sealed class TechniciansFlowTests
{
    private static readonly DateTimeOffset MondayMorning = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly OrgId _tenant = OrgId.New();
    private readonly PostgresFixture _postgres;

    public TechniciansFlowTests(PostgresFixture postgres) => _postgres = postgres;

    [Fact]
    public async Task TakesOnATechnicianThenReplacesTheirSkillsAndShiftInPostgres()
    {
        await using var services = BuildHost();

        var created = await Send(services, new CreateTechnicianCommand(
            "Sam Rivera",
            ["hvac", "electrical"],
            MondayMorning,
            MondayMorning.AddHours(9),
            51.5074,
            -0.1278));
        Assert.True(created.IsSuccess);

        Assert.True((await Send(services, new SetSkillsCommand(created.Value, ["gas", "plumbing"]))).IsSuccess);
        Assert.True((await Send(services, new SetShiftCommand(
            created.Value,
            MondayMorning.AddHours(1),
            MondayMorning.AddHours(11)))).IsSuccess);

        var crew = await Send(services, new ListTechniciansQuery());

        var technician = Assert.Single(crew.Value);
        Assert.Equal(created.Value, technician.Id);

        // The old skills are gone, not merged with the new ones — a set replaced in place and
        // still noticed by the change tracker.
        Assert.Equal(["gas", "plumbing"], technician.Skills);
        Assert.Equal(MondayMorning.AddHours(1), technician.ShiftStart);
        Assert.Equal(MondayMorning.AddHours(11), technician.ShiftEnd);
        Assert.Equal(51.5074, technician.Latitude);
        Assert.Equal(-0.1278, technician.Longitude);
    }

    /// <summary>
    /// A shop types its hours in its own timezone. Npgsql refuses a <c>DateTimeOffset</c> with a
    /// non-zero offset against <c>timestamptz</c>, so without the conversion in the handler this is
    /// not a wrong answer — it is an exception from the driver.
    /// </summary>
    [Fact]
    public async Task AShiftGivenInAnotherOffsetIsStoredAsTheSameInstant()
    {
        await using var services = BuildHost();
        var summerMorning = new DateTimeOffset(2026, 8, 10, 9, 0, 0, TimeSpan.FromHours(2));

        var created = await Send(services, new CreateTechnicianCommand(
            "Ada Okafor",
            ["gas"],
            summerMorning,
            summerMorning.AddHours(8),
            51.5074,
            -0.1278));
        Assert.True(created.IsSuccess);

        var technician = Assert.Single((await Send(services, new ListTechniciansQuery())).Value);

        Assert.Equal(summerMorning, technician.ShiftStart);
        Assert.Equal(TimeSpan.Zero, technician.ShiftStart.Offset);
        Assert.Equal(TimeSpan.FromHours(8), technician.ShiftEnd - technician.ShiftStart);
    }

    private ServiceProvider BuildHost() =>
        TestHost.Over(_postgres).BuildServiceProvider(validateScopes: true);

    /// <summary>One request: one scope, one tenant, one unit of work.</summary>
    private async Task<TResponse> Send<TResponse>(ServiceProvider services, IRequest<TResponse> request)
    {
        using var scope = services.ActingAs(_tenant);

        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }
}
