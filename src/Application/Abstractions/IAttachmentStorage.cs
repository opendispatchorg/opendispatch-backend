using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Application.Abstractions;

/// <summary>
/// Where the bytes of a photograph or a signature actually live.
/// </summary>
/// <remarks>
/// <para>
/// The one port in the system that moves bulk data, and the reason it is a port at all is that the
/// answer changes with the deployment: a self-hosted shop writes to a disk, a managed offering
/// writes to a bucket, and neither is a decision the application layer should be able to see. The
/// adapter that ships is local disk (Document 1's self-hosting promise); an S3 or Azure Blob
/// adapter is one class in Infrastructure and one changed registration, with nothing above it
/// touched.
/// </para>
/// <para>
/// It traffics in <see cref="StorageKey"/> rather than strings on purpose. A key becomes a path or
/// an object name, so a key built from something a caller sent is how directory traversal gets in;
/// the type can only be derived from a tenant and an attachment id, which makes the dangerous call
/// impossible to write rather than merely discouraged.
/// </para>
/// <para>
/// Metadata is not its business. That an attachment exists, whose it is and which job it belongs to
/// is a row in the database written in the same request; this holds only the content, which is why
/// a blob left behind by a failed request is litter rather than corruption — nothing points at it.
/// </para>
/// </remarks>
public interface IAttachmentStorage
{
    /// <summary>
    /// Stores content under a key, replacing anything already there.
    /// </summary>
    /// <param name="key">Where to put it.</param>
    /// <param name="content">The bytes. Read from wherever the caller's stream is positioned.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// Last write wins, which is not a conflict policy so much as an acknowledgement: the key is
    /// derived from an attachment id that only one device holds, so the only thing that can
    /// overwrite a blob is the same capture arriving twice, and the bytes are the same bytes.
    /// </remarks>
    Task SaveAsync(StorageKey key, Stream content, CancellationToken ct);

    /// <summary>
    /// Opens stored content for reading.
    /// </summary>
    /// <param name="key">What to fetch.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>
    /// A readable stream the caller disposes, or <see langword="null"/> if the store has nothing
    /// under that key. Null rather than an exception because a metadata row whose blob is missing is
    /// a state a caller can report on — the alternative is a 500 for a file somebody deleted.
    /// </returns>
    Task<Stream?> OpenAsync(StorageKey key, CancellationToken ct);

    /// <summary>
    /// Deletes stored content, if there is any.
    /// </summary>
    /// <param name="key">What to delete.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// <para>
    /// Here for erasure, which is the only thing in this system that deletes bytes: a photograph is
    /// of somebody's home, and a promise to erase them that left the pictures on a disk would be no
    /// promise at all.
    /// </para>
    /// <para>
    /// <strong>Missing is success.</strong> Nothing under the key means the caller wanted the blob
    /// gone and it is gone. An erasure is retried until it commits, and a retry that failed because
    /// the first attempt had already worked would be a procedure nobody can finish.
    /// </para>
    /// </remarks>
    Task DeleteAsync(StorageKey key, CancellationToken ct);

    /// <summary>
    /// Whether the store can currently be reached at all.
    /// </summary>
    /// <param name="ct">Cancellation.</param>
    /// <returns><see langword="true"/> if this host could store or fetch a blob right now.</returns>
    /// <remarks>
    /// <para>
    /// For readiness, and it is on the port because only an adapter knows what "reachable" means:
    /// a directory that is still mounted, or a bucket that answers with the credentials this host
    /// holds. A health check written outside could only guess.
    /// </para>
    /// <para>
    /// <strong>Reachability, not correctness</strong> — the same line <c>DatabaseHealthCheck</c>
    /// draws. It does not write anything, and a probe that ran every few seconds for the life of a
    /// deployment must not: what it answers is "would an upload have somewhere to go", which is the
    /// question a load balancer is asking.
    /// </para>
    /// <para>
    /// It reports rather than throws, because an unreachable store is an ordinary state of the world
    /// for a probe rather than an error. The reason lands in this host's logs when a request
    /// actually tries to use it.
    /// </para>
    /// </remarks>
    Task<bool> IsReachableAsync(CancellationToken ct);
}
