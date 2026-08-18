using FluentValidation;

namespace OpenDispatch.Application.Scheduling.NormalizeDay;

/// <summary>
/// Shape rules for a repair.
/// </summary>
/// <remarks>
/// The same horizon cap as a re-plan, for a milder version of the same reason: this loads every stop
/// in the stretch and every job behind them, so a caller who fat-fingered a year should find out
/// from a 400 rather than from a slow request.
/// </remarks>
internal sealed class NormalizeDayValidator : AbstractValidator<NormalizeDayCommand>
{
    public NormalizeDayValidator()
    {
        RuleFor(command => command.TechnicianId)
            .Must(technician => technician.Value != Guid.Empty)
            .WithMessage("A day belongs to somebody: name the technician.");

        RuleFor(command => command.To)
            .GreaterThan(command => command.From)
            .WithMessage("A horizon has to close after it opens.");

        RuleFor(command => command.To)
            .Must((command, to) => to - command.From <= OptimizeDay.OptimizeDayValidator.MaxHorizon)
            .WithMessage($"A horizon cannot span more than {OptimizeDay.OptimizeDayValidator.MaxHorizon.TotalDays:0} days.");
    }
}
