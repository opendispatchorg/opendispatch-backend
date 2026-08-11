using FluentValidation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Sync.PullChanges;

/// <summary>
/// Shape rules for a pull.
/// </summary>
/// <remarks>
/// Two, because there are two things to get wrong. A cursor is refused rather than corrected: the
/// edge turns text into a <c>SyncCursor</c> and rejects anything this server did not issue
/// (<c>SyncCursor.TryParse</c>), so a negative one arriving here means a caller built the value
/// itself — and quietly reading it as "from the beginning" would answer a bug with a full resync of
/// the technician's history over a phone connection.
/// </remarks>
internal sealed class PullChangesValidator : AbstractValidator<PullChangesQuery>
{
    public PullChangesValidator()
    {
        RuleFor(query => query.TechnicianId)
            .NotEqual(default(TechnicianId)).WithMessage("A pull must say whose work it is for.");

        RuleFor(query => query.Since.Value)
            .GreaterThanOrEqualTo(0).WithMessage("That is not a cursor this server issued.");
    }
}
