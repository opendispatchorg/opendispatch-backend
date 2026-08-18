using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Technicians.RetireTechnician;

/// <summary>
/// Retires a technician, or puts them back on the crew.
/// </summary>
/// <remarks>
/// Nothing here looks at their planned work, deliberately: retiring somebody does not unpick their
/// day. See <see cref="RetireTechnicianCommand"/> for why taking stops off them silently would be
/// the worse surprise.
/// </remarks>
internal sealed class RetireTechnicianHandler(ITechnicianRepository technicians, IClock clock)
    : IRequestHandler<RetireTechnicianCommand, Result>
{
    public async Task<Result> Handle(RetireTechnicianCommand command, CancellationToken cancellationToken)
    {
        var technician = await technicians.GetAsync(command.Id, cancellationToken).ConfigureAwait(false);

        if (technician is null)
        {
            return Result.Failure(TechnicianErrors.NotFound(command.Id));
        }

        if (command.Retired)
        {
            technician.Retire(clock.UtcNow);
        }
        else
        {
            technician.Reinstate();
        }

        return Result.Success();
    }
}
