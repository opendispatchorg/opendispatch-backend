namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Commits everything the repositories have been told about as one transaction.
/// </summary>
/// <remarks>
/// <para>
/// It exists because a handler frequently changes two aggregates at once — assigning a job
/// writes an <c>Assignment</c> and moves the <c>Job</c> — and half of that landing is worse
/// than neither half landing. The repositories therefore never save; they only stage.
/// </para>
/// <para>
/// This is the whole persistence lifecycle the application layer gets: load through a
/// repository, call intent methods on the aggregate, save once here. There is no
/// <c>Update</c> anywhere, because an aggregate that was loaded is already being watched and
/// re-registering it would be a second way to say the same thing.
/// </para>
/// <para>
/// Handlers rarely call either method themselves. <c>TransactionBehavior</c> opens a
/// transaction around every command, saves, and commits when the result is a success — so a
/// slice writes intent and the pipeline decides whether it is kept.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>
    /// Writes the staged changes.
    /// </summary>
    /// <returns>
    /// How many rows were affected. Reported rather than swallowed because a save that
    /// touched nothing when the caller expected a write is a bug worth being able to see.
    /// </returns>
    Task<int> SaveChangesAsync(CancellationToken ct);

    /// <summary>
    /// Opens a transaction spanning everything that follows until it is committed or rolled
    /// back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SaveChangesAsync"/> is already atomic over one batch, so this is not what
    /// makes a single save all-or-nothing. It is what makes several of them so, and — the
    /// reason it exists at all — what lets work that has already been written be taken back
    /// when the handler decides the request failed after all.
    /// </para>
    /// <para>
    /// One at a time: opening a second while one is still open throws. Nothing in the system
    /// sends a command from inside another, and the pipeline opens exactly one per command.
    /// </para>
    /// </remarks>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct);
}
