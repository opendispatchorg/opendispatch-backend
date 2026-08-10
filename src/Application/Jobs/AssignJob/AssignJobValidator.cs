using FluentValidation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Jobs.AssignJob;

/// <summary>
/// Shape rules for a manual assignment.
/// </summary>
/// <remarks>
/// <para>
/// Two identifiers and an instant, so there is very little to be malformed — and deliberately so.
/// Whether the technician holds the skill, whether the stop fits inside their shift, whether it
/// overlaps the stop before it: all of those are things the engine treats as hard constraints when
/// it <em>plans</em> a day, and none of them is a reason to refuse a dispatcher who is telling the
/// system what is actually going to happen.
/// </para>
/// <para>
/// That is the same argument the architecture already makes about lateness (Document 2 §4): a
/// system that refuses the instruction leaves the dispatcher with a job it will not place and no
/// way to say "I know, do it anyway". What the system may reasonably refuse is work that can no
/// longer be planned at all — finished, cancelled, or already being driven to — and that is the
/// handler's answer, from the domain's own list.
/// </para>
/// </remarks>
internal sealed class AssignJobValidator : AbstractValidator<AssignJobCommand>
{
    public AssignJobValidator()
    {
        RuleFor(command => command.JobId)
            .NotEqual(default(JobId)).WithMessage("A job must be named.");

        RuleFor(command => command.TechnicianId)
            .NotEqual(default(TechnicianId)).WithMessage("A technician must be named.");
    }
}
