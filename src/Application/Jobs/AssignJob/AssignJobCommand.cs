using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Jobs.AssignJob;

/// <summary>
/// Plans a job into a technician's day, by hand: the dispatch board's drag, and the way a
/// dispatcher overrules the optimiser.
/// </summary>
/// <param name="JobId">The work being planned.</param>
/// <param name="TechnicianId">Whose day it goes on.</param>
/// <param name="ScheduledStart">When they are planned to arrive.</param>
/// <remarks>
/// <para>
/// It is deliberately an instruction rather than a request for advice. The optimiser (step 37) and
/// the emergency insert (step 38) work out <em>where</em> a job should go; this one is told, and
/// its job is to make the plan say so. That is why it does not consult the engine: a dispatcher
/// who drags a stop to four o'clock means four o'clock.
/// </para>
/// <para>
/// Returns the identity of the stop it created or moved, because the board has just drawn it and
/// will want to address it.
/// </para>
/// </remarks>
public sealed record AssignJobCommand(JobId JobId, TechnicianId TechnicianId, DateTimeOffset ScheduledStart)
    : ICommand<AssignmentId>;
