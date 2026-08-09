using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.TestSupport.Fixtures;

/// <summary>
/// A shop with more work than <see cref="SmallCity"/> and less character: technicians and jobs
/// scattered across greater London by a seeded generator.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to the hand-written city, and it exists for one reason: a search is only
/// interesting on a problem big enough to have a great many wrong answers. Eight jobs across
/// three technicians is small enough that cheapest insertion is nearly right by accident;
/// thirty across five is not, and that is where an improvement means something.
/// </para>
/// <para>
/// Generated rather than written out because thirty jobs of hand-picked coordinates would be
/// unreadable and nobody would ever check them. Nothing about it is arbitrary at runtime,
/// though: the same arguments give the same day down to the identifiers, so a benchmark can be
/// compared with the one somebody ran last month.
/// </para>
/// <para>
/// Everybody here holds every skill. Skill matching is a hard constraint tested where hard
/// constraints are tested; letting it bite at random would only make some runs quietly smaller
/// than others.
/// </para>
/// </remarks>
public static class BusyDay
{
    private static readonly ImmutableArray<string> Skills = ["hvac", "plumbing", "electrical"];

    /// <summary>The stretch of time being planned, matching the small city's day.</summary>
    public static TimeWindow Day { get; } = new(SmallCity.At(8), SmallCity.At(18));

    /// <summary>
    /// Five technicians and thirty jobs — a plausible day for a mid-sized shop, and the shape
    /// the engine's benchmarks are quoted against.
    /// </summary>
    public static SchedulingProblemBuilder Problem() => Problem(technicians: 5, jobs: 30, seed: 20260810);

    /// <summary>A day of whatever size, laid out the same way every time for a given seed.</summary>
    public static SchedulingProblemBuilder Problem(int technicians, int jobs, int seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(technicians);
        ArgumentOutOfRangeException.ThrowIfNegative(jobs);

        // One generator drawn on in a fixed order, so the nth job is the same job whatever
        // else changes around it.
        var layout = new Random(seed);

        return SchedulingProblemBuilder.Any()
            .Over(Day)
            .Seeded(seed)
            .Staffed([.. Enumerable.Range(1, technicians).Select(n => Technician(n, layout))])
            .Booked([.. Enumerable.Range(1, jobs).Select(n => Job(n, layout))]);
    }

    private static TechPlan Technician(int n, Random layout) => TechPlanBuilder.Any()
        .WithId(TechnicianId.From(Guid.Parse($"44444444-0000-0000-0000-{n:D12}")))
        .Skilled([.. Skills])
        .Working(new TimeWindow(SmallCity.At(8), SmallCity.At(17)))
        .BasedAt(Somewhere(layout, spread: 0.6d))
        .Build();

    private static SchedJob Job(int n, Random layout)
    {
        // Windows three hours wide, opening somewhere in the first seven hours of the day, so
        // most of the work has an opinion about when it happens without any of it being
        // impossible.
        var opens = 8 + layout.Next(0, 7);

        return SchedJobBuilder.Any()
            .WithId(JobId.From(Guid.Parse($"33333333-0000-0000-0000-{n:D12}")))
            .WithSkill(Skills[layout.Next(Skills.Length)])
            .WithPriority((JobPriority)layout.Next(1, 5))
            .At(Somewhere(layout, spread: 1d))
            .InWindow(new TimeWindow(SmallCity.At(opens), SmallCity.At(opens + 3)))
            .Lasting(TimeSpan.FromMinutes(30 + (layout.Next(0, 5) * 15)))
            .Build();
    }

    /// <summary>
    /// A point in greater London. Technicians are drawn from a tighter box than jobs, the way
    /// a shop's vans start nearer the middle than its customers live.
    /// </summary>
    private static GeoPoint Somewhere(Random layout, double spread) => new(
        51.5074d + ((layout.NextDouble() - 0.5d) * 0.12d * spread),
        -0.1278d + ((layout.NextDouble() - 0.5d) * 0.30d * spread));
}
