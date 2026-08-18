using System.Collections.Frozen;
using OpenDispatch.Domain.Common;
using OpenDispatch.Domain.Identifiers;
using OpenDispatch.Domain.ValueObjects;

namespace OpenDispatch.Domain.Attachments;

/// <summary>
/// A photo or a signature captured against a job, and where its bytes are.
/// </summary>
/// <remarks>
/// <para>
/// An aggregate root holding no bytes. What is in the database is the fact that an attachment
/// exists, whose it is, and the key its content is stored under; the content itself goes through
/// <c>IAttachmentStorage</c> to a disk today and to a bucket later. Putting megabytes of photograph
/// in a row would make every query that touches the table slower for the sake of data nothing
/// queries.
/// </para>
/// <para>
/// Its identity comes from the device, which is the whole idempotency story: a technician in a
/// basement captures a photo, the upload times out on the way out of the car park, and the retry
/// carries the same id. The server recognises it and does not store the bytes twice — and, because
/// the id is the primary key, cannot, even if two retries arrive at once.
/// </para>
/// <para>
/// It references its job by <see cref="JobId"/> and cannot see it, so "the job this belongs to is
/// really this tenant's" is a rule one level up, in the handler that has both. That is the same
/// arrangement <c>Invoice</c> has with the job it bills.
/// </para>
/// </remarks>
public sealed class Attachment : AggregateRoot
{
    /// <summary>
    /// What a capture may be.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An allow-list rather than a sniff or a refusal to care, and it is here rather than at the
    /// edge because it is the same kind of rule as the state machine: what this system will hold is
    /// not a property of the request that offered it. A type outside this list cannot be stored,
    /// whichever caller asks.
    /// </para>
    /// <para>
    /// It is what a phone captures — photographs and a signature — and nothing else. Notably absent
    /// is <c>image/svg+xml</c>, which is a document that can carry script rather than a picture, and
    /// which a system that serves attachments back would be handing to a browser. The day the
    /// technician app captures something new, this is the one line that changes, and it changes
    /// deliberately.
    /// </para>
    /// </remarks>
    public static IReadOnlySet<string> AllowedContentTypes { get; } = new[]
    {
        "image/jpeg",
        "image/png",
        "image/webp",
        "image/heic",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    // Materialisation constructor — see the note on Job.
    private Attachment() => ContentType = string.Empty;

    private Attachment(
        AttachmentId id,
        OrgId orgId,
        JobId jobId,
        AttachmentKind kind,
        StorageKey storageKey,
        string contentType,
        long byteLength,
        DateTimeOffset createdAt)
    {
        Id = id;
        OrgId = orgId;
        JobId = jobId;
        Kind = kind;
        StorageKey = storageKey;
        ContentType = contentType;
        ByteLength = byteLength;
        CreatedAt = createdAt;
    }

    /// <summary>The device's own id for the capture, and the idempotency key.</summary>
    public AttachmentId Id { get; private set; }

    /// <summary>The tenant it belongs to.</summary>
    public OrgId OrgId { get; private set; }

    /// <summary>The job it was captured against. A reference, never a navigation property.</summary>
    public JobId JobId { get; private set; }

    /// <summary>Whether it is a photograph or a signature.</summary>
    public AttachmentKind Kind { get; private set; }

    /// <summary>
    /// What the bytes are, as the download will answer with.
    /// </summary>
    /// <remarks>
    /// Stored rather than guessed at read time, and constrained to <see cref="AllowedContentTypes"/>
    /// rather than believed: a content type is what a server tells a browser to do with a file, so
    /// letting a caller choose it freely is letting a caller decide how their upload is treated when
    /// somebody else opens it.
    /// </remarks>
    public string ContentType { get; private set; }

    /// <summary>How many bytes were stored, as the uploader counted them.</summary>
    /// <remarks>
    /// Kept so a list of a job's attachments can say how big each is without asking the store —
    /// which for a bucket adapter would be a network call per row.
    /// </remarks>
    public long ByteLength { get; private set; }

    /// <summary>Where the bytes are, in terms the storage adapter understands.</summary>
    /// <remarks>
    /// Derived rather than given, so nothing a device sends can decide where a file is written.
    /// </remarks>
    public StorageKey StorageKey { get; private set; }

    /// <summary>
    /// When it was captured, by the clock of whoever captured it.
    /// </summary>
    /// <remarks>
    /// The device's instant rather than the server's, for the reason every other field instant is:
    /// a photograph taken in a crawlspace at two and uploaded at six was taken at two, and it is
    /// evidence of what the site looked like then.
    /// </remarks>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Records an attachment a technician captured.
    /// </summary>
    /// <param name="id">The id the device generated for it.</param>
    /// <param name="orgId">The tenant.</param>
    /// <param name="jobId">The job it was captured against.</param>
    /// <param name="kind">Photograph or signature.</param>
    /// <param name="contentType">What the bytes are — one of <see cref="AllowedContentTypes"/>.</param>
    /// <param name="byteLength">How many bytes there are.</param>
    /// <param name="capturedAt">When it was captured, by the device's clock.</param>
    /// <exception cref="DomainException">
    /// It has no id of its own, names no job, is a kind of attachment that does not exist, holds
    /// nothing, or is a kind of file this system does not store.
    /// </exception>
    public static Attachment Create(
        AttachmentId id,
        OrgId orgId,
        JobId jobId,
        AttachmentKind kind,
        string contentType,
        long byteLength,
        DateTimeOffset capturedAt)
    {
        // The id is the whole idempotency guarantee. An empty one would make every attachment
        // captured without one the same attachment, and the second would be quietly discarded as a
        // duplicate of a photograph of somewhere else.
        if (id.Value == Guid.Empty)
        {
            throw new DomainException("An attachment must carry the id its device gave it.");
        }

        if (jobId.Value == Guid.Empty)
        {
            throw new DomainException("An attachment must belong to a job.");
        }

        // Guards against a cast integer arriving from a DTO and becoming a kind nothing knows how
        // to show.
        if (!Enum.IsDefined(kind))
        {
            throw new DomainException($"'{kind}' is not a kind of attachment.");
        }

        if (byteLength <= 0)
        {
            throw new DomainException("An attachment must have some content.");
        }

        // Trimmed and matched without regard to case, because a client sending "IMAGE/JPEG" or a
        // header with a trailing space has sent a JPEG. Parameters ("image/jpeg; charset=…") are not
        // stripped: a capture has no charset, and quietly accepting decoration would widen what this
        // list means.
        var normalized = contentType?.Trim() ?? string.Empty;

        if (!AllowedContentTypes.Contains(normalized))
        {
            throw new DomainException(
                $"'{contentType}' is not a kind of file this system stores. It holds photographs and "
                    + $"signatures: {string.Join(", ", AllowedContentTypes.Order(StringComparer.Ordinal))}.");
        }

        return new Attachment(
            id,
            orgId,
            jobId,
            kind,
            StorageKey.For(orgId, id),
            normalized,
            byteLength,
            capturedAt);
    }
}
