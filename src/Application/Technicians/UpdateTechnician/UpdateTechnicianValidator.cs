using FluentValidation;
using OpenDispatch.Application.Validation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Technicians.UpdateTechnician;

/// <summary>Shape rules for correcting a technician's profile — identical to taking one on.</summary>
internal sealed class UpdateTechnicianValidator : AbstractValidator<UpdateTechnicianCommand>
{
    public UpdateTechnicianValidator()
    {
        RuleFor(command => command.Id)
            .NotEqual(default(TechnicianId)).WithMessage("A technician must be named.");

        RuleFor(command => command.Name)
            .NotEmpty().WithMessage("A technician must have a name.")
            .MaximumLength(TextLimits.Name);

        RuleFor(command => command.Latitude).IsLatitude();
        RuleFor(command => command.Longitude).IsLongitude();
    }
}
