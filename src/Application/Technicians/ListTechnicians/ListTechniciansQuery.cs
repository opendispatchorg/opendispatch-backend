using OpenDispatch.Application.Messaging;

namespace OpenDispatch.Application.Technicians.ListTechnicians;

/// <summary>
/// The whole crew, by name.
/// </summary>
/// <remarks>
/// No parameters and therefore no validator, as with <c>ListCustomersQuery</c>. Unpaged too, and
/// here that is not a compromise: the port is unpaged because the scheduler has to consider every
/// technician to place a job, and a shop with more technicians than fit in memory is not the shop
/// this system is for.
/// </remarks>
public sealed record ListTechniciansQuery : IQuery<IReadOnlyList<TechnicianSummary>>;
