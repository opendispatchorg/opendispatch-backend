using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Technicians.UpdateTechnician;

/// <summary>Corrects a technician's name and home base — everything <c>SetSkills</c>/<c>SetShift</c> do not own.</summary>
/// <param name="Id">Which technician.</param>
/// <param name="Name">Their name, as it appears on the dispatch board.</param>
/// <param name="Latitude">Their home base, in decimal degrees between -90 and 90.</param>
/// <param name="Longitude">Their home base, in decimal degrees between -180 and 180.</param>
/// <remarks>
/// Skills and shift are deliberately not here. Document 3, step 47 names them as their own
/// requests — <c>SetSkillsCommand</c>, <c>SetShiftCommand</c> — and they already exist; this
/// command is only what was missing beside them.
/// </remarks>
public sealed record UpdateTechnicianCommand(TechnicianId Id, string Name, double Latitude, double Longitude)
    : ICommand;
