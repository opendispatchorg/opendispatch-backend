using System.Collections.Immutable;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;
using OpenDispatch.Scheduling.Model;
using OpenDispatch.TestSupport.Builders;

namespace OpenDispatch.TestSupport.Fixtures;

/// <summary>
/// A day in a small service shop: three technicians and eight jobs across central London.
/// The shared fixture the scheduling steps are measured against.
/// </summary>
/// <remarks>
/// <para>
/// Big enough to have interesting answers — several routes, jobs only one technician can
/// take, windows that force an order — and small enough that a person can work out by hand
/// what a good schedule looks like and see at a glance when the engine has done something
/// stupid.
/// </para>
/// <para>
/// Everything about it is fixed: the coordinates, the times, and the identifiers. Ids are
/// hand-written rather than minted, so a route printed by a failing test on one machine names
/// the same jobs as on another, and a schedule can be compared across runs and processes — the
/// property the annealing step's determinism rests on.
/// </para>
/// <para>
/// The city is deliberately <em>solvable</em>: every job's skill is held by someone, and the
/// work fits in the shifts with room for driving. Tests about jobs that cannot be placed add
/// their own job to it rather than breaking the baseline for everyone else.
/// </para>
/// </remarks>
public static class SmallCity
{
    private static readonly DateTimeOffset Midnight = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The stretch of time being planned: 08:00 to 18:00, wide enough to hold every shift.</summary>
    public static TimeWindow Day { get; } = new(At(8), At(18));

    /// <summary>Hvac only, on the longest day, working out of Charing Cross.</summary>
    public static TechPlan Sam { get; } = TechPlanBuilder.Any()
        .WithId(Technician(1))
        .Skilled("hvac")
        .Working(new TimeWindow(At(8), At(17)))
        .BasedAt(new GeoPoint(51.5074d, -0.1278d))
        .Build();

    /// <summary>The only plumber, and hvac as well. Finishes earliest, at four.</summary>
    public static TechPlan Alex { get; } = TechPlanBuilder.Any()
        .WithId(Technician(2))
        .Skilled("plumbing", "HVAC")
        .Working(new TimeWindow(At(8), At(16)))
        .BasedAt(new GeoPoint(51.5250d, -0.0880d))
        .Build();

    /// <summary>The only electrician. Starts an hour after everyone else.</summary>
    public static TechPlan Jordan { get; } = TechPlanBuilder.Any()
        .WithId(Technician(3))
        .Skilled("electrical")
        .Working(new TimeWindow(At(9), At(18)))
        .BasedAt(new GeoPoint(51.4860d, -0.1250d))
        .Build();

    /// <summary>A boiler in Fitzrovia, promised for first thing.</summary>
    public static SchedJob FitzroviaBoiler { get; } = Job(1)
        .At(new GeoPoint(51.5190d, -0.1380d))
        .WithSkill("hvac")
        .WithPriority(JobPriority.High)
        .InWindow(new TimeWindow(At(8), At(11)))
        .Lasting(TimeSpan.FromMinutes(90))
        .Build();

    /// <summary>Air conditioning in Camden, the furthest north of the day.</summary>
    public static SchedJob CamdenAirCon { get; } = Job(2)
        .At(new GeoPoint(51.5390d, -0.1426d))
        .WithSkill("hvac")
        .WithPriority(JobPriority.Normal)
        .InWindow(new TimeWindow(At(9), At(12)))
        .Lasting(TimeSpan.FromMinutes(60))
        .Build();

    /// <summary>A leak in Aldgate. Short, and only Alex can take it.</summary>
    public static SchedJob AldgateLeak { get; } = Job(3)
        .At(new GeoPoint(51.5143d, -0.0755d))
        .WithSkill("plumbing")
        .WithPriority(JobPriority.Normal)
        .InWindow(new TimeWindow(At(8, 30), At(12, 30)))
        .Lasting(TimeSpan.FromMinutes(45))
        .Build();

    /// <summary>A rewire in Stockwell. The longest job of the day at two hours.</summary>
    public static SchedJob StockwellRewire { get; } = Job(4)
        .At(new GeoPoint(51.4720d, -0.1230d))
        .WithSkill("electrical")
        .WithPriority(JobPriority.Normal)
        .InWindow(new TimeWindow(At(10), At(14)))
        .Lasting(TimeSpan.FromMinutes(120))
        .Build();

    /// <summary>A routine service out west in Kensington. The one job worth dropping.</summary>
    public static SchedJob KensingtonService { get; } = Job(5)
        .At(new GeoPoint(51.4990d, -0.1930d))
        .WithSkill("hvac")
        .WithPriority(JobPriority.Low)
        .InWindow(new TimeWindow(At(12), At(16)))
        .Lasting(TimeSpan.FromMinutes(60))
        .Build();

    /// <summary>A burst pipe in Islington, promised for the afternoon.</summary>
    public static SchedJob IslingtonBurstPipe { get; } = Job(6)
        .At(new GeoPoint(51.5362d, -0.1033d))
        .WithSkill("plumbing")
        .WithPriority(JobPriority.High)
        .InWindow(new TimeWindow(At(13), At(16)))
        .Lasting(TimeSpan.FromMinutes(75))
        .Build();

    /// <summary>No power in Southwark. The emergency, and the most expensive job to drop.</summary>
    public static SchedJob SouthwarkNoPower { get; } = Job(7)
        .At(new GeoPoint(51.5040d, -0.0900d))
        .WithSkill("electrical")
        .WithPriority(JobPriority.Emergency)
        .InWindow(new TimeWindow(At(9), At(11)))
        .Lasting(TimeSpan.FromMinutes(60))
        .Build();

    /// <summary>A furnace at the Oval, late in the day.</summary>
    public static SchedJob OvalFurnace { get; } = Job(8)
        .At(new GeoPoint(51.4816d, -0.1130d))
        .WithSkill("hvac")
        .WithPriority(JobPriority.Normal)
        .InWindow(new TimeWindow(At(13), At(17)))
        .Lasting(TimeSpan.FromMinutes(90))
        .Build();

    /// <summary>Everyone working, in a stable order.</summary>
    public static ImmutableArray<TechPlan> Technicians { get; } = [Sam, Alex, Jordan];

    /// <summary>Everything wanting doing, in a stable order.</summary>
    public static ImmutableArray<SchedJob> Jobs { get; } =
    [
        FitzroviaBoiler,
        CamdenAirCon,
        AldgateLeak,
        StockwellRewire,
        KensingtonService,
        IslingtonBurstPipe,
        SouthwarkNoPower,
        OvalFurnace,
    ];

    /// <summary>
    /// The city as a problem, ready to solve or to add to — <c>SmallCity.Problem().Plus(rush).Build()</c>.
    /// </summary>
    public static SchedulingProblemBuilder Problem() => SchedulingProblemBuilder.Any()
        .Over(Day)
        .Staffed([.. Technicians])
        .Booked([.. Jobs]);

    /// <summary>A time on the fixture's day, in UTC.</summary>
    public static DateTimeOffset At(int hour, int minute = 0) =>
        Midnight.AddHours(hour).AddMinutes(minute);

    // Hand-written identifiers, prefixed by kind, so a failing assertion names something a
    // person can find in this file.
    private static TechnicianId Technician(int n) =>
        TechnicianId.From(Guid.Parse($"11111111-0000-0000-0000-{n:D12}"));

    private static SchedJobBuilder Job(int n) =>
        SchedJobBuilder.Any().WithId(JobId.From(Guid.Parse($"22222222-0000-0000-0000-{n:D12}")));
}
