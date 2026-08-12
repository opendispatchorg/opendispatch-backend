namespace OpenDispatch.Contracts.Technicians;

/// <summary>
/// The body of <c>PUT /technicians/{id}</c> — everything <see cref="SetSkillsRequest"/> and
/// <see cref="SetShiftRequest"/> do not own.
/// </summary>
/// <param name="Name">Their name, as it appears on the dispatch board.</param>
/// <param name="Latitude">Their home base, in decimal degrees between -90 and 90.</param>
/// <param name="Longitude">Their home base, in decimal degrees between -180 and 180.</param>
public sealed record UpdateTechnicianRequest(string Name, double Latitude, double Longitude);
