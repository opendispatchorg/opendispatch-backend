using OpenDispatch.Domain.Jobs;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Infrastructure.Seeding;

/// <summary>
/// A stretch of the demo day, stated as offsets from midnight rather than as instants.
/// </summary>
/// <remarks>
/// The dataset has to be loadable on any day — <c>make seed</c> run this morning should produce
/// this morning's work — so nothing in it can name a date. Every window and every shift is an
/// offset, and <see cref="On"/> is where the day it belongs to arrives.
/// </remarks>
/// <param name="Opens">How far into the day it starts.</param>
/// <param name="Closes">How far into the day it ends. Never earlier than <paramref name="Opens"/>.</param>
internal readonly record struct DemoWindow(TimeSpan Opens, TimeSpan Closes)
{
    /// <summary>Places this stretch on a particular day.</summary>
    /// <param name="dayStart">Midnight UTC of the day being seeded.</param>
    public TimeWindow On(DateTimeOffset dayStart) => new(dayStart + Opens, dayStart + Closes);
}

/// <summary>
/// The windows the demo day is built from.
/// </summary>
/// <remarks>
/// <para>
/// Every hour here is UTC, because the system stores instants and models no timezone anywhere
/// (Document 2 leaves an organization's local hours out of scope, and step 48's
/// <c>GET /dispatch/board?day=</c> resolves a date to a UTC calendar day for the same reason).
/// A demo day that ran from 14:00 to 22:30 UTC to read as 07:00–15:30 in Portland would
/// straddle nothing and gain nothing — it would just make the board's own day boundary look
/// arbitrary.
/// </para>
/// <para>
/// The spread is what makes the day a scheduling problem rather than a list: a promised window
/// that opens late is binding on the engine (a technician who arrives early waits), and one that
/// closes early is not (running past it is priced, not forbidden). Both ends are represented
/// here on purpose.
/// </para>
/// </remarks>
internal static class DemoWindows
{
    /// <summary>First thing — before most of the crew has started.</summary>
    public static DemoWindow Early { get; } = new(TimeSpan.FromHours(6.5), TimeSpan.FromHours(9.5));

    /// <summary>The ordinary morning slot.</summary>
    public static DemoWindow Morning { get; } = new(TimeSpan.FromHours(7), TimeSpan.FromHours(12));

    /// <summary>A two-hour promise somebody is waiting in for.</summary>
    public static DemoWindow Midday { get; } = new(TimeSpan.FromHours(10), TimeSpan.FromHours(12));

    /// <summary>The ordinary afternoon slot.</summary>
    public static DemoWindow Afternoon { get; } = new(TimeSpan.FromHours(12), TimeSpan.FromHours(17));

    /// <summary>After the school run, before the shop shuts.</summary>
    public static DemoWindow Late { get; } = new(TimeSpan.FromHours(14), TimeSpan.FromHours(18));

    /// <summary>Any time somebody can get there.</summary>
    public static DemoWindow AnyTime { get; } = new(TimeSpan.FromHours(7), TimeSpan.FromHours(18));
}

/// <summary>One of the crew, before they are a <see cref="Domain.Technicians.Technician"/>.</summary>
/// <param name="Name">Their name, as it appears on the board.</param>
/// <param name="Skills">What they are qualified for. Matched case-insensitively against a job's.</param>
/// <param name="Shift">The hours they are available — a hard constraint on where work can go.</param>
/// <param name="Lat">Latitude of where their day starts and ends.</param>
/// <param name="Lng">Longitude of where their day starts and ends.</param>
internal sealed record DemoTechnician(
    string Name,
    IReadOnlyList<string> Skills,
    DemoWindow Shift,
    double Lat,
    double Lng);

/// <summary>Somebody on the books, and the places they want work done.</summary>
/// <param name="Name">Their name, personal or trading.</param>
/// <param name="Email">An address, or <see langword="null"/>.</param>
/// <param name="Phone">A number, or <see langword="null"/>.</param>
/// <param name="Locations">Where the work happens.</param>
internal sealed record DemoCustomer(
    string Name,
    string? Email,
    string? Phone,
    IReadOnlyList<DemoLocation> Locations);

/// <summary>
/// A place work happens, and the work booked at it.
/// </summary>
/// <remarks>
/// Jobs hang off the location rather than off a flat list keyed by index, so a site and the work
/// at it cannot drift apart while the table is being edited — the commonest way a hand-written
/// dataset ends up with a job pointing at somebody else's address.
/// </remarks>
/// <param name="Label">What the customer calls it.</param>
/// <param name="Address">The postal address a technician is given.</param>
/// <param name="Lat">Latitude, for routing.</param>
/// <param name="Lng">Longitude, for routing.</param>
/// <param name="Jobs">The work booked here for the day.</param>
internal sealed record DemoLocation(
    string Label,
    string Address,
    double Lat,
    double Lng,
    IReadOnlyList<DemoJob> Jobs);

/// <summary>A unit of work, before it is a <see cref="Job"/>.</summary>
/// <param name="Skill">What a technician must hold to take it.</param>
/// <param name="Priority">How badly it needs doing.</param>
/// <param name="Window">What the customer was promised.</param>
/// <param name="Minutes">How long it is expected to take on site.</param>
internal sealed record DemoJob(string Skill, JobPriority Priority, DemoWindow Window, int Minutes);
