namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Something else committed a change to the same aggregate first, so this unit of work's own
/// changes were written against a version that no longer exists.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why an exception at all, in a codebase where expected failures are
/// <c>Result</c>s.</strong> The conflict is discovered inside <c>IUnitOfWork.SaveChangesAsync</c>,
/// which every command reaches through the transaction behavior rather than by calling it and
/// inspecting an answer — there is no return value on that path for a failure to ride home in.
/// It is turned back into a <c>Result</c> one layer up, by <c>ConcurrencyBehavior</c>, so a
/// handler still never has to know this type exists and the caller still gets an ordinary 409.
/// </para>
/// <para>
/// <strong>It exists so that the layer below can say this without naming EF Core.</strong> The
/// persistence adapter catches its provider's own concurrency exception and rethrows this one:
/// <c>Application</c> may not reference EF Core (Document 2 §2), so a behavior here cannot catch
/// <c>DbUpdateConcurrencyException</c> by type. This is the port's vocabulary for that event, in
/// the same folder as the port that raises it.
/// </para>
/// <para>
/// The optimistic concurrency this reports is the <c>Version</c> token on every aggregate root
/// (Document 2 §6). A dispatcher dragging a job that another dispatcher moved while the first was
/// looking at it is the ordinary way to reach it — not a bug, and not something a retry of the
/// same stale write should be encouraged to attempt.
/// </para>
/// </remarks>
public sealed class ConcurrencyConflictException : Exception
{
    /// <summary>Creates the exception with the default message.</summary>
    public ConcurrencyConflictException()
        : base("Another change to this data was committed first.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What happened.</param>
    public ConcurrencyConflictException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception over the provider's own.</summary>
    /// <param name="message">What happened.</param>
    /// <param name="innerException">The provider exception this was translated from.</param>
    public ConcurrencyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
