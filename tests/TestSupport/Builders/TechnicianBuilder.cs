using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Technicians;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.TestSupport.Builders;

/// <summary>
/// Builds <see cref="Technician"/>s for tests.
/// </summary>
/// <example>
/// <code>
/// var alice = TechnicianBuilder.Any().Named("Alice").Skilled("hvac", "electrical").Build();
/// </code>
/// </example>
public sealed record TechnicianBuilder
{
    private static readonly DateTimeOffset MorningOf = new(2026, 8, 10, 8, 0, 0, TimeSpan.Zero);

    private OrgId Org { get; init; } = OrgId.New();

    private string Name { get; init; } = "Sam Rivera";

    private ImmutableArray<string> Skills { get; init; } = ["hvac"];

    private TimeWindow Shift { get; init; } = new(MorningOf, MorningOf.AddHours(9));

    private GeoPoint HomeBase { get; init; } = new(51.5074d, -0.1278d);

    /// <summary>An ordinary technician on an ordinary nine-hour day.</summary>
    public static TechnicianBuilder Any() => new();

    /// <summary>Puts the technician in a particular tenant.</summary>
    public TechnicianBuilder ForOrg(OrgId org) => this with { Org = org };

    /// <summary>Names the technician.</summary>
    public TechnicianBuilder Named(string name) => this with { Name = name };

    /// <summary>Replaces what they are qualified to work on. Pass nothing for a trainee.</summary>
    public TechnicianBuilder Skilled(params string[] skills) => this with { Skills = [.. skills] };

    /// <summary>Sets the hours they are available.</summary>
    public TechnicianBuilder Working(TimeWindow shift) => this with { Shift = shift };

    /// <summary>Sets where their day starts and ends.</summary>
    public TechnicianBuilder BasedAt(GeoPoint homeBase) => this with { HomeBase = homeBase };

    /// <summary>Creates the technician.</summary>
    public Technician Build() => Technician.Create(Org, Name, Skills, Shift, HomeBase);
}
