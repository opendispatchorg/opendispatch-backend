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
}
