using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Technicians.UpdateTechnician;

/// <summary>Loads the technician and corrects their name and home base.</summary>
internal sealed class UpdateTechnicianHandler(ITechnicianRepository technicians)
    : IRequestHandler<UpdateTechnicianCommand, Result>
{
    public async Task<Result> Handle(UpdateTechnicianCommand command, CancellationToken cancellationToken)
    {
        var technician = await technicians.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (technician is null)
        {
            return Result.Failure(TechnicianErrors.NotFound(command.Id));
        }

        technician.Rename(command.Name);
        technician.SetHomeBase(new GeoPoint(command.Latitude, command.Longitude));

        return Result.Success();
    }
}
