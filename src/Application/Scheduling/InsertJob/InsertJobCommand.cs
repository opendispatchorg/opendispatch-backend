using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;

namespace OpenDispatch.Application.Scheduling.InsertJob;

/// <summary>
/// Finds the best place in the day as it stands for one more job, and puts it there.
/// </summary>
/// <param name="JobId">The work that has just come in.</param>
/// <remarks>
/// <para>
/// Emergency dispatch. A boiler has failed at eleven o'clock and the dispatcher needs to know
/// where it fits — not a fresh plan for an afternoon half of which has already been driven. Every
/// stop that is already planned keeps its technician and its place in their run; the only thing
/// that moves is the clock, for the stops after the one slotted in.
/// </para>
/// <para>
/// One parameter, because everything else is already known: which day this is comes from the job's
/// own promised window and the shifts the crew is working, and where the job can go is the whole
/// question being asked.
/// </para>
/// </remarks>
public sealed record InsertJobCommand(JobId JobId) : ICommand<InsertedJob>;
