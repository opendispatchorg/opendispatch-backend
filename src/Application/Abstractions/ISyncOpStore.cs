using OpenDispatch.Application.Sync;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// The op log: what technicians' devices have already done, so that doing it again does nothing.
/// </summary>
/// <remarks>
/// <para>
/// Two methods, because a push needs exactly two things: to find out which of the ops in a batch
/// it has seen before, and to write down the ones it applies. There is no update — an op log
/// entry is a statement about a moment, and editing one would make it a statement about nothing.
/// </para>
/// <para>
/// <see cref="Add"/> stages rather than writes, like every other port here, and that is what
/// makes the guarantee hold: the record of an operation and the change it caused are written by
/// one <see cref="IUnitOfWork"/> save, so there is no window in which a job has been started but
/// the log does not know it — or the reverse, which would silently swallow the retry.
/// </para>
/// <para>
/// Nothing takes an <c>OrgId</c>. Tenant scope is ambient, applied by the persistence layer's
/// global filters (step 29), so one organization's device can never be told that another's op id
/// is already spoken for.
/// </para>
/// </remarks>
public interface ISyncOpStore
{
    /// <summary>
    /// Of these operations, which have already been applied.
    /// </summary>
    /// <remarks>
    /// A batch question rather than one per op: a phone that has been in a basement all morning
    /// pushes its whole queue at once, and asking the database once per op would make the cost of
    /// a bad signal a round trip per action taken while it was gone.
    /// </remarks>
    /// <param name="ids">The ids in the batch being pushed.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The subset already in the log. Empty when every op in the batch is new.</returns>
    Task<IReadOnlySet<SyncOpId>> FindAppliedAsync(IReadOnlyCollection<SyncOpId> ids, CancellationToken ct);

    /// <summary>
    /// Stages the record of an operation that has been applied. It is written when the unit of
    /// work is saved, alongside the change it describes.
    /// </summary>
    void Add(SyncOpRecord op);
}
