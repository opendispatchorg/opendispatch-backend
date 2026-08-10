using OpenDispatch.Application.Messaging;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.Jobs;

namespace OpenDispatch.Application.Jobs.ChangeJobStatus;

/// <summary>
/// Moves a job to the next stage of its life.
/// </summary>
/// <param name="JobId">Which job.</param>
/// <param name="Status">Where it should be.</param>
/// <param name="CompletedAt">
/// When the work actually finished. Only read when <paramref name="Status"/> is
/// <see cref="JobStatus.Completed"/>, and only needed when that is not now.
/// </param>
/// <remarks>
/// <para>
/// A target status rather than a verb, because the caller is a board with buttons and a phone with
/// a list of stages, and both think in terms of where the job should be. Which method that means
/// is this slice's business, and the legality of it is the domain's.
/// </para>
/// <para>
/// <paramref name="CompletedAt"/> is the one concession to the field: a technician finishes a job
/// in a basement at two o'clock and their phone syncs at six, and recording six would put the work
/// four hours after it happened — on the invoice, in the day's numbers, and in anything a
/// <c>JobCompleted</c> handler later does. When it is omitted the handler asks the clock, which is
/// right for a dispatcher pressing the button as the call ends.
/// </para>
/// </remarks>
public sealed record ChangeJobStatusCommand(
    JobId JobId,
    JobStatus Status,
    DateTimeOffset? CompletedAt = null) : ICommand;
