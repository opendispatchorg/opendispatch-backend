using System.Collections.Immutable;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling.Model;

namespace OpenDispatch.TestSupport.Builders;

/// <summary>
/// Builds <see cref="SchedulingProblem"/>s for tests.
/// </summary>
/// <example>
/// <code>
/// var problem = SchedulingProblemBuilder.Any()
///     .Staffed(TechPlanBuilder.Any().Skilled("hvac").Build())
///     .Booked(SchedJobBuilder.Any().WithSkill("plumbing").Build())
///     .Build();
/// </code>
/// </example>
/// <remarks>
/// Starts empty rather than plausible: a problem is the whole input to the engine, so a test
/// that means "one technician and one job" should be able to say exactly that and nothing
/// else. For a realistic day, start from
/// <see cref="Fixtures.SmallCity.Problem"/> instead.
/// </remarks>
public sealed record SchedulingProblemBuilder
{
    /// <summary>
    /// The seed every fixture problem carries unless a test says otherwise, so two runs of
    /// the same test see the same schedule.
    /// </summary>
    public const int DefaultSeed = 20260810;

    private TimeWindow Horizon { get; init; } =
        new(TechPlanBuilder.ShiftStart, TechPlanBuilder.ShiftStart.AddHours(10));

    private ImmutableArray<TechPlan> Technicians { get; init; } = [];

    private ImmutableArray<SchedJob> Jobs { get; init; } = [];

    private Objective Weights { get; init; } = Objective.Default;

    private int Seed { get; init; } = DefaultSeed;

    /// <summary>An empty ten-hour day: nobody to work it, nothing to do.</summary>
    public static SchedulingProblemBuilder Any() => new();

    /// <summary>Sets the stretch of time being planned.</summary>
    public SchedulingProblemBuilder Over(TimeWindow horizon) => this with { Horizon = horizon };

    /// <summary>Replaces who is available.</summary>
    public SchedulingProblemBuilder Staffed(params TechPlan[] technicians) =>
        this with { Technicians = [.. technicians] };

    /// <summary>Replaces what wants doing.</summary>
    public SchedulingProblemBuilder Booked(params SchedJob[] jobs) => this with { Jobs = [.. jobs] };

    /// <summary>Adds one more technician to whoever is already available.</summary>
    public SchedulingProblemBuilder Plus(TechPlan technician) =>
        this with { Technicians = Technicians.Add(technician) };

    /// <summary>Adds one more job to whatever already wants doing.</summary>
    public SchedulingProblemBuilder Plus(SchedJob job) => this with { Jobs = Jobs.Add(job) };

    /// <summary>Sets what a good schedule is worth — usually to switch a term off.</summary>
    public SchedulingProblemBuilder Weighted(Objective weights) => this with { Weights = weights };

    /// <summary>Sets the seed the engine's randomness runs from.</summary>
    public SchedulingProblemBuilder Seeded(int seed) => this with { Seed = seed };

    /// <summary>Creates the problem.</summary>
    public SchedulingProblem Build() => new(Horizon, Technicians, Jobs, Weights, Seed);
}
