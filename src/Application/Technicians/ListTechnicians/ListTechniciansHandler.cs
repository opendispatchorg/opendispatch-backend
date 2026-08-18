using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.Technicians;

namespace OpenDispatch.Application.Technicians.ListTechnicians;

/// <summary>
/// Reads the crew and summarises them.
/// </summary>
/// <remarks>
/// <para>
/// This one sorts, where <c>ListCustomersHandler</c> deliberately does not — because one port
/// serves two readers with different needs. <c>ITechnicianRepository.ListAsync</c> orders by id,
/// and that ordering is load-bearing: the scheduler consumes the crew in the order it arrives, and
/// Document 2 §4 requires the same problem to produce the same plan under a seed. A person reading
/// a crew list wants it alphabetical, and that is not a reason to change what the scheduler gets.
/// </para>
/// <para>
/// So the reader's order is applied to the reader's projection. The alternative — a second ordered
/// query on the port — buys nothing while the whole crew is read anyway.
/// </para>
/// </remarks>
internal sealed class ListTechniciansHandler(ITechnicianRepository technicians)
    : IRequestHandler<ListTechniciansQuery, Result<IReadOnlyList<TechnicianSummary>>>
{
    public async Task<Result<IReadOnlyList<TechnicianSummary>>> Handle(
        ListTechniciansQuery query,
        CancellationToken cancellationToken)
    {
        var crew = await technicians.ListAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<TechnicianSummary> summaries =
        [
            .. crew
                .OrderBy(technician => technician.Name, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(technician => technician.Id.Value)
                .Select(Project),
        ];

        return Result.Success(summaries);
    }

    private static TechnicianSummary Project(Technician technician) => new(
        technician.Id,
        technician.Name,
        [.. technician.Skills.Order(StringComparer.OrdinalIgnoreCase)],
        technician.Shift.Start,
        technician.Shift.End,
        technician.HomeBase.Lat,
        technician.HomeBase.Lng,
        technician.RetiredAt);
}
