using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using OpenDispatch.Application.Abstractions;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Infrastructure.Attachments;

/// <summary>
/// Attachment content in an S3-compatible bucket.
/// </summary>
/// <remarks>
/// <para>
/// The adapter a deployment on a container platform needs, and the reason is starker than
/// scaling: on Render, Fly, Cloud Run and every other platform that hands a container a fresh
/// filesystem, the disk <c>LocalDiskAttachmentStorage</c> writes to is destroyed by the next
/// deploy. The bytes it holds are the only ones in the system that are not in Postgres, so a deploy
/// would silently take every photograph and signature the field has captured. Where the container
/// is ephemeral this is not the better adapter, it is the only correct one.
/// </para>
/// <para>
/// <strong>S3-compatible rather than Amazon.</strong> Path-style addressing over a configured
/// endpoint is the dialect Cloudflare R2, Backblaze B2, MinIO and Amazon itself all answer, so one
/// class serves every store a self-hosting shop is likely to have — which is the point, given
/// Document 1's promise that the business owns its own data and picks where it lives. Virtual-host
/// addressing (<c>bucket.host</c>) is deliberately not used: it needs DNS per bucket, which a
/// MinIO on a laptop does not have.
/// </para>
/// <para>
/// <strong>The key is the object name, unchanged.</strong> <see cref="StorageKey"/> is already two
/// uuids and a slash, derived from a tenant and an attachment and never from anything a caller
/// sent, so it needs no escaping and cannot address anything outside this bucket. The slash gives
/// the bucket a prefix per organization, which is what makes a listing — and a lifecycle rule —
/// per tenant.
/// </para>
/// <para>
/// <strong>Deleting is deleting.</strong> Erasure is the one thing in this system that destroys
/// bytes, and a bucket with versioning or object-lock retention turned on answers a delete by
/// hiding the object rather than removing it — which would leave a photograph of somebody's home
/// recoverable after they were told it was gone. The bucket must not be configured that way; the
/// runbook says so, because it is not something this code can check for a caller.
/// </para>
/// </remarks>
internal sealed class ObjectStoreAttachmentStorage : IAttachmentStorage, IDisposable
{
    private readonly IAmazonS3 _s3;
    private readonly string _bucket;
    private readonly ILogger<ObjectStoreAttachmentStorage> _log;

    /// <summary>Creates the adapter over one bucket.</summary>
    /// <param name="s3">The client, owned by this instance and disposed with it.</param>
    /// <param name="bucket">The bucket content lives in. It must already exist.</param>
    /// <param name="log">Where to say what was written, which is what an operator needs to find it.</param>
    public ObjectStoreAttachmentStorage(IAmazonS3 s3, string bucket, ILogger<ObjectStoreAttachmentStorage> log)
    {
        ArgumentNullException.ThrowIfNull(s3);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);

        _s3 = s3;
        _bucket = bucket;
        _log = log;
    }

    public async Task SaveAsync(StorageKey key, Stream content, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);

        await _s3.PutObjectAsync(
            new PutObjectRequest
            {
                BucketName = _bucket,
                Key = key.Value,
                InputStream = content,

                // The caller owns the stream — the upload handler is inside a `using`, and closing
                // it here would break a retry that wanted to re-read it.
                AutoCloseStream = false,

                // The content type is metadata this system already holds in the attachment row and
                // serves from there, so it is not written twice. What the bucket returns is never
                // read: a store that echoed back a type a device claimed would be a second, weaker
                // source for the one thing the download's allow-list exists to control.
            },
            ct).ConfigureAwait(false);

        AttachmentStorageLog.Stored(_log, key.Value);
    }

    public async Task<Stream?> OpenAsync(StorageKey key, CancellationToken ct)
    {
        try
        {
            var stored = await _s3.GetObjectAsync(_bucket, key.Value, ct).ConfigureAwait(false);

            // The caller disposes it, which also disposes the response and returns the connection.
            return stored.ResponseStream;
        }
        catch (AmazonS3Exception missing) when (IsMissingObject(missing))
        {
            // A metadata row whose blob is missing is a state a caller can report on — the port
            // says null, and the download answers 404 rather than 500.
            return null;
        }
    }

    public async Task DeleteAsync(StorageKey key, CancellationToken ct)
    {
        // S3 answers a delete of an object that is not there with success, which is the behaviour
        // the port asks for: an erasure that is retried must be able to finish.
        await _s3.DeleteObjectAsync(_bucket, key.Value, ct).ConfigureAwait(false);

        AttachmentStorageLog.Deleted(_log, key.Value);
    }

    public void Dispose() => _s3.Dispose();

    /// <summary>
    /// Whether a failure means "no such object" rather than "no such bucket".
    /// </summary>
    /// <remarks>
    /// Both are 404, and conflating them is how a misconfigured bucket name turns into every
    /// photograph quietly 404ing instead of the host saying it cannot reach its store. Only the
    /// first is the port's null; the second is thrown, which is what a mistyped bucket deserves.
    /// </remarks>
    private static bool IsMissingObject(AmazonS3Exception failure) =>
        failure.StatusCode == HttpStatusCode.NotFound
        && !string.Equals(failure.ErrorCode, "NoSuchBucket", StringComparison.Ordinal);
}
