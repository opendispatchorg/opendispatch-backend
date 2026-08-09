using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.TestSupport.Builders;

/// <summary>
/// Builds <see cref="SchedJob"/>s for tests, so a scheduling test states only the one fact it
/// is about.
/// </summary>
/// <example>
/// <code>
/// var job = SchedJobBuilder.Any().WithSkill("plumbing").Lasting(TimeSpan.FromMinutes(45)).Build();
/// </code>
/// </example>
public sealed record SchedJobBuilder
{
    /// <summary>The same fixed morning the other builders use, so windows line up across tests.</summary>
    public static readonly DateTimeOffset MorningOf = new(2026, 8, 10, 9, 0, 0, TimeSpan.Zero);

    private JobId Id { get; init; } = JobId.New();

    private GeoPoint Point { get; init; } = new(51.5074d, -0.1278d);

    private string Skill { get; init; } = "hvac";

    private JobPriority Priority { get; init; } = JobPriority.Normal;

    private TimeWindow Window { get; init; } = new(MorningOf, MorningOf.AddHours(3));

    private TimeSpan Duration { get; init; } = TimeSpan.FromHours(1);

    /// <summary>An ordinary hour of hvac work, promised for the morning.</summary>
    public static SchedJobBuilder Any() => new();

    /// <summary>Pins the job's identity — for tests that assert where a particular job ended up.</summary>
    public SchedJobBuilder WithId(JobId id) => this with { Id = id };

    /// <summary>Puts the job somewhere on the map.</summary>
    public SchedJobBuilder At(GeoPoint point) => this with { Point = point };

    /// <summary>Sets the skill a technician needs to take the job.</summary>
    public SchedJobBuilder WithSkill(string skill) => this with { Skill = skill };

    /// <summary>Sets how badly the job needs doing.</summary>
    public SchedJobBuilder WithPriority(JobPriority priority) => this with { Priority = priority };

    /// <summary>Sets the window promised to the customer.</summary>
    public SchedJobBuilder InWindow(TimeWindow window) => this with { Window = window };

    /// <summary>Sets how long the work takes on site.</summary>
    public SchedJobBuilder Lasting(TimeSpan duration) => this with { Duration = duration };

    /// <summary>Creates the job.</summary>
    public SchedJob Build() => new(Id, Point, Skill, Priority, Window, Duration);
}
