using FluentValidation;
using OpenDispatch.Application.Validation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Jobs.CreateJob;

/// <summary>
/// Shape rules for booking a job.
/// </summary>
/// <remarks>
/// <para>
/// Every rule here stands in front of something <c>Job.Create</c> or <c>TimeWindow</c> also
/// refuses — an unnamed skill, work expected to take no time, a priority that is not one, a window
/// that ends before it starts. The domain keeps refusing because those are what a job <em>is</em>;
/// these refuse first so the caller is told which field was wrong instead of being handed an
/// exception.
/// </para>
/// <para>
/// The skill is measured against the same limit as a technician's, because it is the same
/// vocabulary: a job asks for what a technician has, and a required skill nobody could have typed
/// on the other screen is a job nobody will ever be assigned to.
/// </para>
/// </remarks>
internal sealed class CreateJobValidator : AbstractValidator<CreateJobCommand>
{
    public CreateJobValidator()
    {
        RuleFor(command => command.CustomerId)
            .NotEqual(default(CustomerId)).WithMessage("A customer must be named.");

        RuleFor(command => command.LocationId)
            .NotEqual(default(ServiceLocationId)).WithMessage("A service location must be named.");

        RuleFor(command => command.RequiredSkill)
            .NotEmpty().WithMessage("A job must state the skill it requires.")
            .MaximumLength(TextLimits.Skill);

        // A cast integer arriving from a DTO is the case this catches: without it, an unknown
        // priority reaches the objective function as a weight nothing expects.
        RuleFor(command => command.Priority)
            .IsInEnum().WithMessage("That is not a priority a job can have.");

        RuleFor(command => command.WindowEnd)
            .GreaterThanOrEqualTo(command => command.WindowStart)
            .WithMessage("A job's window cannot end before it starts.");

        RuleFor(command => command.EstimatedDuration)
            .GreaterThan(TimeSpan.Zero).WithMessage("A job must be expected to take some time.");
    }
}
