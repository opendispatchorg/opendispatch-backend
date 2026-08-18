using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Technicians.ListTechnicians;

/// <summary>
/// A technician as a crew list shows them.
/// </summary>
/// <param name="Id">Their identity.</param>
/// <param name="Name">Their name, as it appears on the dispatch board.</param>
/// <param name="Skills">What they are qualified to work on, alphabetically.</param>
/// <param name="ShiftStart">When their working hours open.</param>
/// <param name="ShiftEnd">When their working hours close.</param>
/// <param name="Latitude">Their home base, in decimal degrees.</param>
/// <param name="Longitude">Their home base, in decimal degrees.</param>
/// <remarks>
/// <para>
/// It carries the whole technician, which is the opposite of what <c>CustomerSummary</c> does —
/// and the difference is real rather than an inconsistency. A customer's detail grows without
/// limit as they accumulate sites, so a list of full details is every address of every customer
/// sent to a picker that shows a name. A technician is fixed-size and small: their skills and
/// shift are the two things a crew screen exists to show, and this step defines no per-technician
/// query to fetch them separately.
/// </para>
/// <para>
/// The skills are sorted here rather than left in the set's enumeration order, so two identical
/// crews always render the same way round.
/// </para>
/// </remarks>
/// <param name="RetiredAt">When they were taken off the books, or <see langword="null"/> while current.</param>
public sealed record TechnicianSummary(
    TechnicianId Id,
    string Name,
    IReadOnlyList<string> Skills,
    DateTimeOffset ShiftStart,
    DateTimeOffset ShiftEnd,
    double Latitude,
    double Longitude,
    DateTimeOffset? RetiredAt);
