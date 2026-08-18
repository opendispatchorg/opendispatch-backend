using FluentValidation;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Sync.PullChanges;

/// <summary>
/// Shape rules for a pull.
/// </summary>
/// <remarks>
/// Three, because there are three things to get wrong. A cursor is refused rather than corrected: the
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

        // Not a caller's mistake — the edge supplies this from configuration — but a page of
        // nothing is a device that can never advance, and this is where that is caught rather than
        // in a technician's van. The options binding refuses it at startup too; this is the second
        // lock on the same door, for the day something else builds this query.
        RuleFor(query => query.MaxTransactions)
            .GreaterThan(0).WithMessage("A pull must be allowed to carry at least one transaction.");
    }
}
