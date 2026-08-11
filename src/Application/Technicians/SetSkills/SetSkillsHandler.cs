using MediatR;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Application.Results;

namespace OpenDispatch.Application.Technicians.SetSkills;

/// <summary>
/// Loads the technician and hands them the list.
/// </summary>
/// <remarks>
/// There is nothing between the load and the intent method on purpose. Anything this handler did
/// to reconcile the old set with the new one would be the aggregate's rule about skill identity,
/// restated outside the aggregate.
/// </remarks>
internal sealed class SetSkillsHandler(ITechnicianRepository technicians)
    : IRequestHandler<SetSkillsCommand, Result>
{
    public async Task<Result> Handle(SetSkillsCommand command, CancellationToken cancellationToken)
    {
        var technician = await technicians
            .GetAsync(command.TechnicianId, cancellationToken)
            .ConfigureAwait(false);

        if (technician is null)
        {
            return Result.Failure(TechnicianErrors.NotFound(command.TechnicianId));
        }

        technician.SetSkills(command.Skills);

        return Result.Success();
    }
}
