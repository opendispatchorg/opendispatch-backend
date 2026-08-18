using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Application.Technicians.ListTechnicians;
using OpenDispatch.Domain.Technicians;

namespace OpenDispatch.Application.Technicians.GetTechnician;

/// <summary>Loads the technician and projects them.</summary>
internal sealed class GetTechnicianHandler(ITechnicianRepository technicians)
    : IRequestHandler<GetTechnicianQuery, Result<TechnicianSummary>>
{
    public async Task<Result<TechnicianSummary>> Handle(
        GetTechnicianQuery query,
        CancellationToken cancellationToken)
    {
        var technician = await technicians.GetAsync(query.Id, cancellationToken).ConfigureAwait(false);

        return technician is null
            ? Result.Failure<TechnicianSummary>(TechnicianErrors.NotFound(query.Id))
            : Result.Success(Project(technician));
    }

    // Mirrors ListTechniciansHandler.Project. Not shared: two call sites of a few lines each is
    // duplication cheap enough to keep, and a third caller is the moment to extract it.
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
