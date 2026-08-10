namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// An open transaction, held for as long as the work it covers.
/// </summary>
/// <remarks>
/// <para>
/// Disposing without committing rolls back. That is the safe default rather than a courtesy:
/// the pipeline commits deliberately, and every other way out of a handler — a failed result,
/// an exception, a cancelled request — should leave the database as it found it.
/// </para>
/// <para>
/// Deliberately smaller than the database transaction underneath it. No isolation level, no
/// savepoints, no nesting: the application layer has one thing to say about a transaction,
/// which is whether the work inside it is kept.
/// </para>
/// </remarks>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    /// <summary>Keeps everything written since the transaction was opened.</summary>
    Task CommitAsync(CancellationToken ct);

    /// <summary>Discards everything written since the transaction was opened.</summary>
    Task RollbackAsync(CancellationToken ct);
}
