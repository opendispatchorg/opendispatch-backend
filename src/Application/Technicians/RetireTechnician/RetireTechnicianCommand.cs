using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Technicians.RetireTechnician;

/// <summary>
/// Takes a technician off the crew, or puts them back on.
/// </summary>
/// <param name="Id">Whose record.</param>
/// <param name="Retired"><see langword="true"/> to retire them, <see langword="false"/> to reinstate.</param>
/// <remarks>
/// <para>
/// Somebody leaves. Their name is on every stop they ever drove and every audit entry they wrote,
/// so there is no delete; what a shop means by "remove them" is that they stop being planned a day
/// and stop appearing in the dropdown. A retired technician is left out of
/// <c>ITechnicianRepository.ListAsync</c>, which is what both schedulers and the office's list ask.
/// </para>
/// <para>
/// <strong>Work already planned is left exactly where it is.</strong> Retiring somebody does not
/// unpick their day — a stop they are driving to this afternoon is still theirs, and taking it off
/// them silently would be a worse surprise than the one this prevents. Re-plan the day, or move the
/// stops by hand, and the next optimise will not offer them again.
/// </para>
/// <para>
/// It does not switch off their login either. That is <c>disable-user</c>, deliberately separate: a
/// person leaving the crew and a person losing their sign-in are different acts on different
/// records, and a shop reorganising its rota should not be revoking credentials as a side effect.
/// </para>
/// </remarks>
public sealed record RetireTechnicianCommand(TechnicianId Id, bool Retired) : ICommand;
