using FluentValidation;

namespace OpenDispatch.Application.Scheduling.OptimizeDay;

/// <summary>
/// Shape rules for a re-plan.
/// </summary>
/// <remarks>
/// The weights are the interesting half. Each one is a price, and the engine's <c>Objective</c>
/// refuses a negative or non-finite price by throwing: a negative price is a reward for the thing
/// it is meant to discourage, and an infinite one is a hard constraint in disguise. Refusing them
/// here names the weight that was wrong instead of letting the caller find out through a 500.
/// </remarks>
internal sealed class OptimizeDayValidator : AbstractValidator<OptimizeDayCommand>
{
    public OptimizeDayValidator()
    {
        RuleFor(command => command.To)
            .GreaterThanOrEqualTo(command => command.From)
            .WithMessage("A horizon cannot end before it starts.");

        When(command => command.Weights is not null, () =>
        {
            RuleFor(command => command.Weights!.Travel).IsAPrice();
            RuleFor(command => command.Weights!.Lateness).IsAPrice();
            RuleFor(command => command.Weights!.Overtime).IsAPrice();
            RuleFor(command => command.Weights!.Unassigned).IsAPrice();
        });
    }
}

/// <summary>The rule every objective weight follows.</summary>
file static class PriceRules
{
    /// <summary>Refuses anything that is not a finite, non-negative price.</summary>
    public static IRuleBuilderOptions<T, double> IsAPrice<T>(this IRuleBuilderInitial<T, double> rule) =>
        rule
            .Cascade(CascadeMode.Stop)
            .Must(double.IsFinite).WithMessage("A weight must be a number.")
            .GreaterThanOrEqualTo(0d).WithMessage("A weight cannot be negative.");
}
