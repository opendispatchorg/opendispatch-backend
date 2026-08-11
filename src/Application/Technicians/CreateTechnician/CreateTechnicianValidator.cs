using FluentValidation;
using OpenDispatch.Application.Validation;

namespace OpenDispatch.Application.Technicians.CreateTechnician;

/// <summary>
/// Shape rules for taking on a technician.
/// </summary>
/// <remarks>
/// Every rule here mirrors something the domain or a value object also refuses — a nameless
/// technician, a blank skill, an inverted window, a coordinate that is not a place. That is not
/// redundancy: the domain refuses because the rule is what a technician <em>is</em>, and these
/// refuse first so the caller is told which field was wrong instead of being handed an exception.
/// Delete a rule here and the invariant still holds, as a 500.
/// </remarks>
internal sealed class CreateTechnicianValidator : AbstractValidator<CreateTechnicianCommand>
{
    public CreateTechnicianValidator()
    {
        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("A technician must have a name.")
            .MaximumLength(TextLimits.Name);

        // The list may be empty — a trainee matches no skilled job, which the domain allows on
        // purpose — but it may not be missing, and no skill in it may be blank.
        RuleFor(command => command.Skills)
            .NotNull().WithMessage("A technician's skills must be listed, even if there are none.");

        RuleForEach(command => command.Skills)
            .NotEmpty().WithMessage("A skill must be named.")
            .MaximumLength(TextLimits.Skill);

        RuleFor(command => command.ShiftEnd)
            .GreaterThanOrEqualTo(command => command.ShiftStart)
            .WithMessage("A shift cannot end before it starts.");

        RuleFor(command => command.Latitude).IsLatitude();
        RuleFor(command => command.Longitude).IsLongitude();
    }
}
