using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// One technician's day: a lane on the timeline and a line on the map.
/// </summary>
/// <param name="TechnicianId">Whose day it is.</param>
/// <param name="Name">Their name, as the lane is labelled.</param>
/// <param name="Skills">
/// What they are qualified for, alphabetically. Here because a manual assignment may put work on
/// somebody who does not hold the job's skill — the optimiser never will, but a dispatcher
/// overruling it is a deliberate part of the design — and the board is the only thing that can
/// show it. Without this the lane carries half of a comparison whose other half
/// (<see cref="BoardJob.RequiredSkill"/>) is already on every block.
/// </param>
/// <param name="Shift">The hours they are available, which is what the lane is drawn against.</param>
/// <param name="HomeBase">Where the day starts and ends — the first and last leg of the drawn route run from here.</param>
/// <param name="Stops">Their run, in sequence order.</param>
public sealed record BoardRoute(
    TechnicianId TechnicianId,
    string Name,
    IReadOnlyList<string> Skills,
    TimeWindow Shift,
    GeoPoint HomeBase,
    IReadOnlyList<BoardStop> Stops);
