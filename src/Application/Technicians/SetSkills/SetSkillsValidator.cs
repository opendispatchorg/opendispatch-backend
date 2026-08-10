using FluentValidation;
using OpenDispatch.Application.Validation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Technicians.SetSkills;

/// <summary>
/// Shape rules for replacing a technician's skills.
/// </summary>
/// <remarks>
/// The same rules the create command applies to the same list, because it is the same list — a
/// skill that cannot be typed when taking somebody on cannot be typed a week later either.
/// Duplicates are not refused: the aggregate holds a set, so naming a skill twice is a caller
/// being redundant rather than a caller being wrong.
/// </remarks>
internal sealed class SetSkillsValidator : AbstractValidator<SetSkillsCommand>
{
    public SetSkillsValidator()
    {
        RuleFor(command => command.TechnicianId)
            .NotEqual(default(TechnicianId)).WithMessage("A technician must be named.");

        RuleFor(command => command.Skills)
            .NotNull().WithMessage("A technician's skills must be listed, even if there are none.");

        RuleForEach(command => command.Skills)
            .NotEmpty().WithMessage("A skill must be named.")
            .MaximumLength(TextLimits.Skill);
    }
}
