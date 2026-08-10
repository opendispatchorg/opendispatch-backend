using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Technicians.SetSkills;

/// <summary>
/// Replaces what a technician is qualified to work on.
/// </summary>
/// <param name="TechnicianId">Whose skills to replace.</param>
/// <param name="Skills">The complete list they should have afterwards. Empty makes them a trainee again.</param>
/// <remarks>
/// <para>
/// It states the whole set rather than a change to it, because the screen behind it is a list of
/// checkboxes and the caller knows what the answer should be, not which boxes moved. That also
/// makes it idempotent: a re-sent or replayed update leaves the technician in the same place,
/// which matters once the same shape travels over the sync path.
/// </para>
/// <para>
/// The difference between what they have and what they should have is worked out inside the
/// aggregate, by <c>Technician.SetSkills</c>. It has to be: two skills are one skill when they
/// differ only in case or whitespace, and a handler computing the difference with an ordinary
/// comparer would either churn them or, worse, keep both.
/// </para>
/// </remarks>
public sealed record SetSkillsCommand(TechnicianId TechnicianId, IReadOnlyList<string> Skills)
    : ICommand;
