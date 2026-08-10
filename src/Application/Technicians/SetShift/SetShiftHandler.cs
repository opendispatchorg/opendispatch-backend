using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Technicians.SetShift;

/// <summary>
/// Loads the technician and replaces their hours.
/// </summary>
/// <remarks>
/// The instants are converted to UTC before the window is built, for the reason set out on
/// <c>CreateTechnicianHandler</c>: the column is <c>timestamptz</c> and Npgsql refuses a non-zero
/// offset, while the offset itself is something nothing in the system reads.
/// </remarks>
internal sealed class SetShiftHandler(ITechnicianRepository technicians)
    : IRequestHandler<SetShiftCommand, Result>
{
    public async Task<Result> Handle(SetShiftCommand command, CancellationToken cancellationToken)
    {
        var technician = await technicians
            .GetAsync(command.TechnicianId, cancellationToken)
            .ConfigureAwait(false);

        if (technician is null)
        {
            return Result.Failure(TechnicianErrors.NotFound(command.TechnicianId));
        }

        technician.SetShift(new TimeWindow(command.Start.ToUniversalTime(), command.End.ToUniversalTime()));

        return Result.Success();
    }
}
