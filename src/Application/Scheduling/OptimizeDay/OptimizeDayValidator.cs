using FluentValidation;

namespace OpenDispatch.Application.Scheduling.OptimizeDay;

/// <summary>
/// Shape rules for a re-plan.
/// </summary>
/// <remarks>
/// <para>
/// The weights are the interesting half. Each one is a price, and the engine's <c>Objective</c>
/// refuses a negative or non-finite price by throwing: a negative price is a reward for the thing
/// it is meant to discourage, and an infinite one is a hard constraint in disguise. Refusing them
/// here names the weight that was wrong instead of letting the caller find out through a 500.
/// </para>
/// <para>
/// The horizon cap is the other half, and it exists for a caller rather than for the engine: nothing
/// about <c>Objective</c> refuses a decade, but every schedulable job in it would be loaded into an
/// O(n²) search (step 37's own entry named this and left it for the controller). Ninety days is
/// generous against what a re-plan is actually for — reworking a day or a week gone wrong — and
/// still small enough that a caller who fat-fingered a year finds out from a 400, not a slow request.
/// </para>
/// </remarks>
internal sealed class OptimizeDayValidator : AbstractValidator<OptimizeDayCommand>
{
    /// <summary>
    /// The longest horizon a single re-plan may span. See the class remarks for why ninety days.
    /// </summary>
    internal static readonly TimeSpan MaxHorizon = TimeSpan.FromDays(90);

    public OptimizeDayValidator()
    {
        RuleFor(command => command.To)
            .GreaterThanOrEqualTo(command => command.From)
            .WithMessage("A horizon cannot end before it starts.");

        RuleFor(command => command.To)
            .Must((command, to) => to - command.From <= MaxHorizon)
            .WithMessage($"A horizon cannot span more than {MaxHorizon.TotalDays:0} days.");

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
