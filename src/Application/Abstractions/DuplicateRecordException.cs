namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// A row this unit of work tried to write is one the database already holds, and holds uniquely —
/// so somebody else wrote it first.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <see cref="ConcurrencyConflictException"/>, and it exists for the same reason:
/// two people acting on one board at once is an ordinary Tuesday, and the collision has to reach
/// the caller as a refusal rather than as a broken server. The difference is which lock caught it.
/// A stale <c>Version</c> catches two writers of a row that already existed; a unique index catches
/// two writers <em>creating</em> the row — two dispatchers planning the same job at the same
/// moment, or two re-plans of one day running side by side, both of which end at the one-stop-per-job
/// index (<c>AssignmentConfiguration</c>) rather than at a version check, because neither writer
/// read a version to be stale about.
/// </para>
/// <para>
/// Translated in the persistence adapter for the reason its sibling is: <c>Application</c> may not
/// name EF Core's or Npgsql's exceptions (Document 2 §2), so the port needs a word of its own for
/// what happened. <c>ConcurrencyBehavior</c> turns it into an ordinary <c>Result</c>, and the
/// caller gets the same 409 a lost version race gets — under its own code, because "reload and
/// look again" and "somebody has already planned this" are different things for a client to say.
/// </para>
/// </remarks>
public sealed class DuplicateRecordException : Exception
{
    /// <summary>Creates the exception with the default message.</summary>
    public DuplicateRecordException()
        : base("A record that must be one of a kind was written twice.")
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What happened.</param>
    public DuplicateRecordException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception over the provider's own.</summary>
    /// <param name="message">What happened.</param>
    /// <param name="innerException">The provider exception this was translated from.</param>
    public DuplicateRecordException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
