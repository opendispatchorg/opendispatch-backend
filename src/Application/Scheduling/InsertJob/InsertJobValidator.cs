using FluentValidation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Scheduling.InsertJob;

/// <summary>
/// Shape rules for an emergency insertion.
/// </summary>
/// <remarks>
/// One field, so there is one rule. Everything else this command can be refused for — the job is
/// already planned, the work is under way, the day will not take it — needs the day itself, and is
/// the handler's answer as a conflict rather than the validator's as a malformed request.
/// </remarks>
internal sealed class InsertJobValidator : AbstractValidator<InsertJobCommand>
{
    public InsertJobValidator() =>
        RuleFor(command => command.JobId)
            .NotEqual(default(JobId)).WithMessage("A job must be named.");
}
