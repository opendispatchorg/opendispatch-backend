using System.Diagnostics.CodeAnalysis;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Scheduling.Model;

/// <summary>
/// One job placed in one technician's day: when they get there, when they start, when they
/// finish, and how long the drive was.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Arrival"/> and <see cref="Start"/> are separate on purpose. A technician who
/// reaches a job before its window opens waits, and that gap is real: it is capacity the
/// schedule is spending and the reason a tighter route is not always a better one. Collapsing
/// the two would hide it.
/// </para>
/// <para>
/// <see cref="TravelMin"/> is the drive that got them <em>here</em> — from the previous stop,
/// or from the technician's home base for the first stop of the day. Attributing travel to
/// the stop it precedes is what lets a route's cost be summed stop by stop.
/// </para>
/// <para>
/// The window a customer was promised is nowhere in here. A stop that runs late is a
/// perfectly valid stop; how much that costs is the objective's business, not this type's.
/// </para>
/// </remarks>
/// <param name="JobId">The job being done.</param>
/// <param name="Arrival">When the technician reaches the site.</param>
/// <param name="Start">When work begins. Never before <paramref name="Arrival"/>; later if they wait.</param>
/// <param name="End">When work finishes. Never before <paramref name="Start"/>.</param>
/// <param name="TravelMin">Minutes of driving to get here from the previous stop or the home base.</param>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "'Stop' is the name the architecture and build plan give this concept, and a dispatcher's word for it. The rule guards against VB consumers, which a C#-only backend with TypeScript clients does not have.")]
public sealed record Stop(
    JobId JobId,
    DateTimeOffset Arrival,
    DateTimeOffset Start,
    DateTimeOffset End,
    double TravelMin)
{
    /// <summary>When work begins. Never before <see cref="Arrival"/>.</summary>
    public DateTimeOffset Start { get; } = Start >= Arrival
        ? Start
        : throw new ArgumentOutOfRangeException(
            nameof(Start), Start, "A technician cannot start work before arriving.");

    /// <summary>When work finishes. Never before <see cref="Start"/>.</summary>
    public DateTimeOffset End { get; } = End >= Start
        ? End
        : throw new ArgumentOutOfRangeException(
            nameof(End), End, "A stop cannot finish before it starts.");

    /// <summary>Minutes of driving to reach this stop. Finite and never negative.</summary>
    public double TravelMin { get; } = double.IsFinite(TravelMin) && TravelMin >= 0d
        ? TravelMin
        : throw new ArgumentOutOfRangeException(
            nameof(TravelMin), TravelMin, "Travel to a stop must be a finite, non-negative number of minutes.");

    /// <summary>How long the technician is on site.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>How long they wait on site before they can start. Zero on a stop reached no earlier than needed.</summary>
    public TimeSpan Wait => Start - Arrival;
}
