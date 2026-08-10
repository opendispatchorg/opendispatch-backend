using FluentValidation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Technicians.SetShift;

/// <summary>
/// Shape rules for replacing a shift.
/// </summary>
/// <remarks>
/// A zero-length shift is allowed and means a day off: <c>TimeWindow</c> permits it, and refusing
/// it here would invent a rule the domain does not have. Only an end before a start is nonsense,
/// and it is refused here so the caller is told rather than being handed the
/// <c>ArgumentOutOfRangeException</c> the value object would throw.
/// </remarks>
internal sealed class SetShiftValidator : AbstractValidator<SetShiftCommand>
{
    public SetShiftValidator()
    {
        RuleFor(command => command.TechnicianId)
            .NotEqual(default(TechnicianId)).WithMessage("A technician must be named.");

        RuleFor(command => command.End)
            .GreaterThanOrEqualTo(command => command.Start)
            .WithMessage("A shift cannot end before it starts.");
    }
}
