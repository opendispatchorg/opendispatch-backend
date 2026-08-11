using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Technicians.CreateTechnician;

/// <summary>
/// Takes on a technician: who they are, what they can work on, when they work, and where their
/// day starts and ends.
/// </summary>
/// <param name="Name">Their name, as it appears on the dispatch board.</param>
/// <param name="Skills">What they are qualified to work on. Empty is allowed — a trainee simply matches no skilled job.</param>
/// <param name="ShiftStart">When their working hours open.</param>
/// <param name="ShiftEnd">When their working hours close. Never earlier than <paramref name="ShiftStart"/>.</param>
/// <param name="Latitude">Their home base, in decimal degrees between -90 and 90.</param>
/// <param name="Longitude">Their home base, in decimal degrees between -180 and 180.</param>
/// <remarks>
/// <para>
/// Every field the scheduler consumes and nothing else, because that is what a technician is here
/// (Document 2 §4): skills for the hard skill-match constraint, a shift for the hard
/// fits-in-the-day constraint, a home base to measure the first and last drive from.
/// </para>
/// <para>
/// The shift arrives as two instants rather than as a <c>TimeWindow</c>, for the same reason the
/// coordinate arrives as two doubles: an inverted window throws at construction, so a command
/// carrying one would be built at the edge before any validator ran, and an end before a start
/// would surface as an unhandled exception rather than as a rejected field.
/// </para>
/// </remarks>
public sealed record CreateTechnicianCommand(
    string Name,
    IReadOnlyList<string> Skills,
    DateTimeOffset ShiftStart,
    DateTimeOffset ShiftEnd,
    double Latitude,
    double Longitude) : ICommand<TechnicianId>;
