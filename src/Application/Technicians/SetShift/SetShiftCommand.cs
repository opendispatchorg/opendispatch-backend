using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Technicians.SetShift;

/// <summary>
/// Replaces the hours a technician is available over the planning horizon.
/// </summary>
/// <param name="TechnicianId">Whose hours to replace.</param>
/// <param name="Start">When their working hours open.</param>
/// <param name="End">When their working hours close. Never earlier than <paramref name="Start"/>.</param>
/// <remarks>
/// This is the constraint the scheduler treats as hard — work must fit inside the shift, unlike
/// the customer's promised window, which it may run past at a penalty. Moving it after a day is
/// planned does not re-plan anything: assignments are a separate aggregate, and re-optimising is
/// step 37's job.
/// </remarks>
public sealed record SetShiftCommand(TechnicianId TechnicianId, DateTimeOffset Start, DateTimeOffset End)
    : ICommand;
