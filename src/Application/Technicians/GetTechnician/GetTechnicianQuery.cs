using OpenDispatch.Application.Messaging;
using OpenDispatch.Application.Technicians.ListTechnicians;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Technicians.GetTechnician;

/// <summary>Fetches one technician.</summary>
/// <param name="Id">Which technician.</param>
/// <remarks>
/// Answers with <see cref="TechnicianSummary"/>, the same shape the crew list already uses —
/// <c>TechnicianSummary</c>'s own remarks explain why a technician has one shape rather than two:
/// fixed-size, and this step defines the first per-technician reader of it.
/// </remarks>
public sealed record GetTechnicianQuery(TechnicianId Id) : IQuery<TechnicianSummary>;
