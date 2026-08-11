using FluentValidation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Jobs.ChangeJobStatus;

/// <summary>
/// Shape rules for a status change.
/// </summary>
/// <remarks>
/// <para>
/// It refuses a status this request cannot drive — <c>Unscheduled</c>, which nothing returns to,
/// and <c>Invoiced</c> and <c>Paid</c>, which invoicing drives through the invoice aggregate. That
/// is a malformed request rather than a conflict: the caller asked this endpoint for something it
/// does not do, whatever state the job happens to be in.
/// </para>
/// <para>
/// It does <em>not</em> check whether the move is legal from where the job is now. That needs the
/// job, it is the state machine's answer rather than the request's, and it is a
/// <c>Conflict</c> — 409 — where this is a 400. Validators here never touch state.
/// </para>
/// </remarks>
internal sealed class ChangeJobStatusValidator : AbstractValidator<ChangeJobStatusCommand>
{
    public ChangeJobStatusValidator()
    {
        RuleFor(command => command.JobId)
            .NotEqual(default(JobId)).WithMessage("A job must be named.");

        RuleFor(command => command.Status)
            .Must(JobIntents.Drivable.ContainsKey)
            .WithMessage("A job cannot be moved to that status by this request.");
    }
}
