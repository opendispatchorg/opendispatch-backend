using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// One technician's day: a lane on the timeline and a line on the map.
/// </summary>
/// <param name="TechnicianId">Whose day it is.</param>
/// <param name="Name">Their name, as the lane is labelled.</param>
/// <param name="Shift">The hours they are available, which is what the lane is drawn against.</param>
/// <param name="HomeBase">Where the day starts and ends — the first and last leg of the drawn route run from here.</param>
/// <param name="Stops">Their run, in sequence order.</param>
public sealed record BoardRoute(
    TechnicianId TechnicianId,
    string Name,
    TimeWindow Shift,
    GeoPoint HomeBase,
    IReadOnlyList<BoardStop> Stops);
