using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.TestSupport.Builders;

/// <summary>
/// Builds <see cref="TechPlan"/>s for tests.
/// </summary>
/// <example>
/// <code>
/// var alex = TechPlanBuilder.Any().Skilled("hvac", "plumbing").Working(nineToFive).Build();
/// </code>
/// </example>
public sealed record TechPlanBuilder
{
    /// <summary>The start of the fixture working day, matching <see cref="SchedJobBuilder.MorningOf"/>'s date.</summary>
    public static readonly DateTimeOffset ShiftStart = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private TechnicianId Id { get; init; } = TechnicianId.New();

    private ImmutableArray<string> Skills { get; init; } = ["hvac"];

    private TimeWindow Shift { get; init; } = new(ShiftStart, ShiftStart.AddHours(9));

    private GeoPoint HomeBase { get; init; } = new(51.5074d, -0.1278d);

    /// <summary>An hvac technician on an ordinary nine-hour day.</summary>
    public static TechPlanBuilder Any() => new();

    /// <summary>Pins the technician's identity — for tests that assert whose route a job landed on.</summary>
    public TechPlanBuilder WithId(TechnicianId id) => this with { Id = id };

    /// <summary>Replaces what they are qualified for. Pass nothing for a trainee.</summary>
    public TechPlanBuilder Skilled(params string[] skills) => this with { Skills = [.. skills] };

    /// <summary>Sets the hours they are available.</summary>
    public TechPlanBuilder Working(TimeWindow shift) => this with { Shift = shift };

    /// <summary>Sets where their day starts and ends.</summary>
    public TechPlanBuilder BasedAt(GeoPoint homeBase) => this with { HomeBase = homeBase };

    /// <summary>Creates the plan.</summary>
    public TechPlan Build() => new(Id, Skills, Shift, HomeBase);
}
