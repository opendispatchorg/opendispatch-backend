using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Scheduling.NormalizeDay;

/// <summary>
/// Re-times one technician's day so it is a run somebody could actually drive, without changing who
/// does what or in which order.
/// </summary>
/// <param name="TechnicianId">Whose day to repair.</param>
/// <param name="From">When the stretch being repaired opens.</param>
/// <param name="To">When it closes.</param>
/// <remarks>
/// <para>
/// <strong>The way back from a day that overlaps itself.</strong> A manual assignment is a
/// dispatcher's instruction and is carried out literally — nothing re-times the run around it — so
/// two stops can end up on the same hour. That day is not a route, which means the emergency-insert
/// path has nothing to insert into and refuses (<c>schedule.overlappingDay</c>): until now the only
/// answer was to re-optimise, which throws away every decision the dispatcher made about everybody
/// else's day as well.
/// </para>
/// <para>
/// This changes nothing but the clock. The technician keeps their work, in the order they were given
/// it — read from the clock rather than the stored sequence, because a hand-dragged day leaves the
/// numbering behind and the order a run is driven in is the order of the day.
/// </para>
/// <para>
/// <strong>Stops move later, never earlier.</strong> The time a stop is planned for is a promise
/// somebody made to a customer, so re-timing pushes work along to make room and never pulls it
/// forward into a morning nobody was told about.
/// </para>
/// </remarks>
public sealed record NormalizeDayCommand(TechnicianId TechnicianId, DateTimeOffset From, DateTimeOffset To)
    : ICommand<NormalizedDay>;

/// <summary>
/// What the repair did.
/// </summary>
/// <param name="Stops">How many stops the technician has in that stretch.</param>
/// <param name="Moved">How many of them had to move. Zero means the day was already drivable.</param>
/// <param name="FirstStart">When the run now begins, or <see langword="null"/> if there is no work.</param>
/// <param name="LastEnd">When it now ends, or <see langword="null"/> if there is no work.</param>
public sealed record NormalizedDay(int Stops, int Moved, DateTimeOffset? FirstStart, DateTimeOffset? LastEnd);
