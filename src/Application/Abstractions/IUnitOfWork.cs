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
}
